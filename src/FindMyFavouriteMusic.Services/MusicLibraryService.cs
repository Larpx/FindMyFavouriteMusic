using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 音乐库管理服务，负责目录扫描、歌曲入库、喜欢标记及查询等业务编排。
/// </summary>
/// <remarks>
/// 该服务是业务层的核心编排者，依赖音频解码、特征提取、向量序列化等底层组件，
/// 通过仓储模式（ISongRepository）与数据层解耦，通过 IProfileService 协同维护用户画像。
/// 扫描流程采用 SemaphoreSlim 限流的并发模型，兼顾吞吐与资源占用。
/// </remarks>
public class MusicLibraryService : IMusicLibraryService
{
    private readonly IAudioDecoder _audioDecoder;
    private readonly IAcousticFeatureExtractor _acousticExtractor;
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly ISongRepository _songRepository;
    private readonly IProfileService _profileService;
    private readonly IModelOperationLock _modelLock;
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly ScanOptions _scanOptions;
    private readonly ILogger<MusicLibraryService> _logger;
    // 信号量：限制并发处理数，避免一次性解码大量音频导致 OOM 或 CPU 过载
    private readonly SemaphoreSlim _semaphore;

    /// <summary>
    /// 构造函数，通过 DI 注入所有依赖组件。
    /// </summary>
    public MusicLibraryService(
        IAudioDecoder audioDecoder,
        IAcousticFeatureExtractor acousticExtractor,
        IDeepFeatureExtractor deepExtractor,
        IVectorSerializer vectorSerializer,
        ISongRepository songRepository,
        IProfileService profileService,
        IModelOperationLock modelLock,
        IOptions<FeatureExtractionOptions> featureOptions,
        IOptions<ScanOptions> scanOptions,
        ILogger<MusicLibraryService> logger)
    {
        _audioDecoder = audioDecoder;
        _acousticExtractor = acousticExtractor;
        _deepExtractor = deepExtractor;
        _vectorSerializer = vectorSerializer;
        _songRepository = songRepository;
        _profileService = profileService;
        _modelLock = modelLock;
        _featureOptions = featureOptions.Value;
        _scanOptions = scanOptions.Value;
        _logger = logger;
        // 初始化信号量，并发上限由配置 MaxConcurrentProcessing 决定
        _semaphore = new SemaphoreSlim(_scanOptions.MaxConcurrentProcessing);
    }

    /// <summary>
    /// 异步扫描指定目录下的音频文件，提取特征并入库。
    /// </summary>
    /// <param name="directoryPath">待扫描的目录路径</param>
    /// <param name="progress">进度上报回调（百分比 0-100），可为空</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>扫描成功入库的歌曲 DTO 列表</returns>
    /// <remarks>
    /// 编排流程：枚举文件 → 按扩展名过滤 → 并发处理（SemaphoreSlim 限流）→ 进度上报。
    /// 并发模型说明：每个文件处理前获取信号量，处理完毕后释放，确保同时在途的任务数不超过上限。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> ScanDirectoryAsync(
        string directoryPath, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Result<IReadOnlyList<SongDto>>.Failure($"目录不存在: {directoryPath}");
        }

        // 与模型加载互斥：整次扫描持锁，避免中途 Dispose Session
        await using var gate = await _modelLock.AcquireAsync(ct);

        try
        {
            // 防御性检查：配置缺失时 SupportedExtensions 可能为空，回退到默认值
            var extensions = _scanOptions.SupportedExtensions;
            if (extensions is null || extensions.Count == 0)
            {
                extensions = [".mp3", ".wav", ".flac", ".ogg", ".m4a"];
                _logger.LogWarning("ScanOptions.SupportedExtensions 为空，使用默认扩展名列表");
            }

            // 枚举所有文件并按配置的扩展名白名单过滤，使用 OrdinalIgnoreCase 保证跨平台一致性
            var files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                return Result<IReadOnlyList<SongDto>>.Success([]);
            }

            _logger.LogInformation("扫描到 {Count} 个音频文件", files.Count);

            // 已处理计数器，使用 Interlocked 保证线程安全递增
            var processed = 0;
            // 结果列表，使用 lock 保护写入；之所以未用 ConcurrentBag 是为了保留顺序可读性
            var songs = new List<SongDto>();

            // 通过 Select + Task.WhenAll 启动所有任务；实际并发由 _semaphore 控制
            var tasks = files.Select(async file =>
            {
                // 等待信号量，超过并发上限时阻塞当前任务
                await _semaphore.WaitAsync(ct);
                try
                {
                    // 已持有模型锁，走 Core 避免重入死锁
                    var result = await ProcessSongCoreAsync(file, ct);
                    if (result.IsSuccess && result.Value is not null)
                    {
                        // 加锁保护 List 写入，避免多线程同时 Add 导致数据损坏
                        lock (songs)
                        {
                            songs.Add(result.Value);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("处理文件失败: {FilePath}, {Error}", file, result.Error);
                    }

                    // 原子递增已完成数，避免使用锁带来的性能开销
                    Interlocked.Increment(ref processed);
                    // 上报百分比进度，调用方可在 UI 线程刷新进度条
                    progress?.Report((int)((double)processed / files.Count * 100));
                }
                finally
                {
                    // 必须在 finally 中释放信号量，防止异常导致信号量泄漏
                    _semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return Result<IReadOnlyList<SongDto>>.Success(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扫描目录失败: {DirectoryPath}", directoryPath);
            return Result<IReadOnlyList<SongDto>>.Failure(ex);
        }
    }

    /// <summary>
    /// 切换歌曲喜欢状态，并同步更新用户画像。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="isLiked">是否喜欢</param>
    /// <returns>操作结果</returns>
    /// <remarks>
    /// 画像更新策略：
    /// 标记喜欢：调用增量更新（O(1) 复杂度），仅将新歌曲特征加入均值向量；
    /// 取消喜欢：必须全量重建画像，因为均值向量无法"减去"某首歌曲的贡献。
    /// 画像更新失败不回滚喜欢状态，仅记录警告，保证用户操作可用性优先。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result> ToggleLikeAsync(int songId, bool isLiked)
    {
        // Like no-op：状态未变则不写库、不更新画像，避免重复 Like 污染均值
        var songResult = await _songRepository.GetByIdAsync(songId);
        if (!songResult.IsSuccess)
        {
            return songResult;
        }

        var song = songResult.Value!;
        if (song.IsLiked == isLiked)
        {
            _logger.LogDebug("喜欢状态未变，跳过: SongId={SongId}, IsLiked={IsLiked}", songId, isLiked);
            return Result.Success();
        }

        var result = await _songRepository.UpdateLikeStatusAsync(songId, isLiked);
        if (!result.IsSuccess)
        {
            return result;
        }

        if (isLiked)
        {
            // 增量更新：将新喜欢歌曲的特征并入画像，复杂度 O(1)
            var updateResult = await _profileService.UpdateProfileIncrementalAsync(songId);
            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("画像增量更新失败: {Error}", updateResult.Error);
            }
        }
        else
        {
            // 取消喜欢无法"减去"已聚合的均值，只能从剩余喜欢歌曲全量重建（空喜欢会清空画像）
            var rebuildResult = await _profileService.RebuildProfileAsync();
            if (!rebuildResult.IsSuccess)
            {
                _logger.LogWarning("画像重建失败: {Error}", rebuildResult.Error);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// 获取所有标记为喜欢的歌曲。
    /// </summary>
    /// <returns>喜欢的歌曲 DTO 列表</returns>
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> GetLikedSongsAsync()
    {
        var result = await _songRepository.GetLikedSongsAsync();
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<SongDto>>.Failure(result.Error!, result.Exception);
        }

        // 通过 MapToDto 转换为 DTO，避免将 BLOB 等内部字段暴露给展示层
        var dtos = (result.Value ?? []).Select(MapToDto).ToList();
        return Result<IReadOnlyList<SongDto>>.Success(dtos);
    }

    /// <summary>
    /// 获取库中所有歌曲。
    /// </summary>
    /// <returns>全部歌曲 DTO 列表</returns>
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> GetAllSongsAsync()
    {
        var result = await _songRepository.GetAllSongsAsync();
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<SongDto>>.Failure(result.Error!, result.Exception);
        }

        var dtos = (result.Value ?? []).Select(MapToDto).ToList();
        return Result<IReadOnlyList<SongDto>>.Success(dtos);
    }

    /// <summary>
    /// 处理单首歌曲：解码、特征提取并入库。
    /// </summary>
    /// <param name="filePath">音频文件绝对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>入库后的歌曲 DTO</returns>
    /// <remarks>
    /// 幂等性设计：先按 FilePath 查询，若已存在则直接返回，避免重复解码与特征提取。
    /// 例外：若已入库歌曲缺少深度向量且当前模型已加载（典型场景：先无模型扫描入库，
    /// 之后加载模型再重新扫描），则补全深度特征并更新数据库，避免历史歌曲永远无法参与深度预测。
    /// 解码失败时不阻断流程，仅不填充特征向量，仍将基础信息入库以便后续手动补全。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result<SongDto>> ProcessSongAsync(string filePath, CancellationToken ct = default)
    {
        await using var gate = await _modelLock.AcquireAsync(ct);
        return await ProcessSongCoreAsync(filePath, ct);
    }

    /// <summary>
    /// 处理单首歌曲：解码、特征提取并入库（调用方须已持有模型锁）。
    /// </summary>
    private async Task<Result<SongDto>> ProcessSongCoreAsync(string filePath, CancellationToken ct)
    {
        try
        {
            // 幂等性检查：已入库的歌曲判断是否需要补全深度特征
            var existingResult = await _songRepository.GetByFilePathAsync(filePath);
            if (existingResult.IsSuccess && existingResult.Value is not null)
            {
                var existing = existingResult.Value;
                // 补全场景：歌曲已入库但无深度向量，且当前模型已加载
                // 触发场景：用户先在无模型状态下扫描入库，之后加载模型重新扫描
                if (existing.DeepVectorBlob is null && _deepExtractor.IsModelLoaded)
                {
                    _logger.LogInformation("补全歌曲深度特征: {FilePath}", filePath);
                    var supplemented = await SupplementDeepVectorAsync(existing, ct);
                    return Result<SongDto>.Success(MapToDto(supplemented));
                }

                return Result<SongDto>.Success(MapToDto(existing));
            }

            // 构造新歌曲实体，标题默认取文件名（无扩展名），艺术家暂留空待元数据补全
            var song = new Song
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = null,
                IsLiked = false
            };

            byte[]? acousticBlob = null;
            byte[]? deepBlob = null;

            // 解码阶段：失败则跳过特征提取，仍将基础信息入库
            var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
            if (decodeResult.IsSuccess && decodeResult.Value is not null)
            {
                var samples = decodeResult.Value;
                // 声学特征提取（同步方法，计算量较小）
                var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
                if (acousticResult.IsSuccess && acousticResult.Value is not null)
                {
                    acousticBlob = _vectorSerializer.Serialize(acousticResult.Value);
                    song.AcousticVector = acousticResult.Value;
                    song.AcousticVectorBlob = acousticBlob;
                }

                // 深度特征仅在 ONNX 模型加载成功时提取，避免无谓调用
                if (_deepExtractor.IsModelLoaded)
                {
                    var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
                    if (deepResult.IsSuccess && deepResult.Value is not null)
                    {
                        deepBlob = _vectorSerializer.Serialize(deepResult.Value);
                        song.DeepVector = deepResult.Value;
                        song.DeepVectorBlob = deepBlob;
                    }
                }
            }

            var insertResult = await _songRepository.InsertAsync(song);
            if (!insertResult.IsSuccess)
            {
                return Result<SongDto>.Failure(insertResult.Error!, insertResult.Exception);
            }

            song.Id = insertResult.Value;
            return Result<SongDto>.Success(MapToDto(song));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理歌曲失败: {FilePath}", filePath);
            return Result<SongDto>.Failure(ex);
        }
    }

    /// <summary>
    /// 为已入库但缺少深度向量的歌曲补全深度特征。
    /// </summary>
    /// <param name="song">已入库的歌曲实体（含 Id 与 FilePath）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>补全深度向量后的歌曲实体（已更新到数据库）</returns>
    /// <remarks>
    /// 补全流程：解码音频 → 提取深度特征 → 序列化为 BLOB → 更新数据库。
    /// 解码或提取失败时返回原歌曲实体（不更新数据库），保证可用性优先。
    /// </remarks>
    private async Task<Song> SupplementDeepVectorAsync(Song song, CancellationToken ct)
    {
        try
        {
            var decodeResult = await _audioDecoder.DecodeAsync(song.FilePath, ct);
            if (!decodeResult.IsSuccess || decodeResult.Value is null)
            {
                _logger.LogWarning("补全深度特征时解码失败: {FilePath}, {Error}", song.FilePath, decodeResult.Error);
                return song;
            }

            var samples = decodeResult.Value;
            var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
            if (!deepResult.IsSuccess || deepResult.Value is null)
            {
                _logger.LogWarning("补全深度特征时提取失败: {FilePath}, {Error}", song.FilePath, deepResult.Error);
                return song;
            }

            // 序列化深度向量并更新数据库
            song.DeepVector = deepResult.Value;
            song.DeepVectorBlob = _vectorSerializer.Serialize(deepResult.Value);

            // 仅更新深度向量字段，保留原声学向量与喜欢状态
            var updateResult = await _songRepository.UpdateVectorsAsync(
                song.Id, song.AcousticVectorBlob, song.DeepVectorBlob);
            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("更新深度向量到数据库失败: {FilePath}, {Error}", song.FilePath, updateResult.Error);
                // 数据库更新失败时仍返回带深度向量的实体，调用方本次能用到深度特征
            }

            _logger.LogInformation("歌曲深度特征补全成功: {FilePath}", song.FilePath);
            return song;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补全深度特征异常: {FilePath}", song.FilePath);
            return song;
        }
    }

    /// <summary>
    /// 将 Song 实体转换为 SongDto，隔离数据层与展示层。
    /// </summary>
    /// <param name="song">歌曲实体</param>
    /// <returns>展示用 DTO，仅包含必要字段（不暴露原始向量数据）</returns>
    private static SongDto MapToDto(Song song) => new()
    {
        Id = song.Id,
        FilePath = song.FilePath,
        Title = song.Title,
        Artist = song.Artist,
        IsLiked = song.IsLiked,
        // 仅告知 UI 是否已提取特征，用于显示"可预测"等状态
        HasFeatures = song.AcousticVectorBlob is not null
    };
}
