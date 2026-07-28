using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
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
    private readonly IAudioTagService _audioTagService;
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
        IAudioTagService audioTagService,
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
        _audioTagService = audioTagService;
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
    /// 幂等性设计：按 FilePath 查询已有记录后，结合 FileMd5 与深度模型契约决定是否跳过：
    /// <para>1. MD5 未变且声学特征可用、深度契约匹配 → 跳过重算；</para>
    /// <para>2. MD5 未变但深度缺失或模型类型/维度不匹配 → 仅补全深度；</para>
    /// <para>3. MD5 变化或缺声学特征 → 全量重提并 UpdateFeatures；</para>
    /// <para>4. 单文件超过 <see cref="AudioLimits.MaxFileSizeBytes"/> → 直接失败。</para>
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
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return Result<SongDto>.Failure($"文件不存在: {filePath}");
            }

            if (fileInfo.Length > AudioLimits.MaxFileSizeBytes)
            {
                return Result<SongDto>.Failure(
                    $"文件过大（{fileInfo.Length / (1024.0 * 1024):F1}MB），超过硬限制 {AudioLimits.MaxFileSizeDisplay}");
            }

            var md5 = await FileContentHasher.ComputeMd5HexAsync(filePath, ct);
            var format = AudioFormatDetector.DetectFromExtension(filePath).ToString();

            var existingResult = await _songRepository.GetByFilePathAsync(filePath);
            if (existingResult.IsSuccess && existingResult.Value is not null)
            {
                var existing = existingResult.Value;
                var md5Unchanged = string.Equals(existing.FileMd5, md5, StringComparison.OrdinalIgnoreCase);
                var hasAcoustic = existing.AcousticVectorBlob is not null;

                // MD5 未变且声学特征可用 → 可跳过解码；深度按模型类型/维度判定是否需补全
                if (md5Unchanged && hasAcoustic)
                {
                    if (NeedsDeepSupplement(existing))
                    {
                        _logger.LogInformation("MD5 未变，补全/刷新深度特征: {FilePath}", filePath);
                        var supplemented = await SupplementDeepVectorAsync(existing, md5, fileInfo.Length, format, ct);
                        return Result<SongDto>.Success(MapToDto(supplemented));
                    }

                    _logger.LogDebug("MD5 未变且契约匹配，跳过重算: {FilePath}", filePath);
                    return Result<SongDto>.Success(MapToDto(existing));
                }

                // 文件内容变化或缺少声学特征 → 重新提取并更新
                _logger.LogInformation("文件已变更或缺少声学特征，重新提取: {FilePath}", filePath);
                var refreshed = await ExtractAndFillAsync(existing, filePath, md5, fileInfo.Length, format, ct);
                var updateResult = await _songRepository.UpdateFeaturesAsync(refreshed);
                if (!updateResult.IsSuccess)
                {
                    return Result<SongDto>.Failure(updateResult.Error!, updateResult.Exception);
                }

                return Result<SongDto>.Success(MapToDto(refreshed));
            }

            var song = new Song
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = null,
                IsLiked = false,
                FileMd5 = md5,
                FileSize = fileInfo.Length,
                Format = format,
                SourceId = MusicSourceIds.Local
            };

            ApplyTagsToSong(song);
            song = await ExtractAndFillAsync(song, filePath, md5, fileInfo.Length, format, ct);

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

    /// <summary>深度向量缺失，或模型类型/维度与当前加载模型不一致时需要补全。</summary>
    private bool NeedsDeepSupplement(Song song)
    {
        if (!_deepExtractor.IsModelLoaded)
        {
            return false;
        }

        var expectedType = _deepExtractor.ModelType.ToString();
        var expectedDim = _deepExtractor.FeatureDimension;
        return song.DeepVectorBlob is null
               || !string.Equals(song.DeepModelType, expectedType, StringComparison.OrdinalIgnoreCase)
               || song.DeepDim != expectedDim;
    }

    private async Task<Song> ExtractAndFillAsync(
        Song song, string filePath, string md5, long fileSize, string format, CancellationToken ct)
    {
        song.FileMd5 = md5;
        song.FileSize = fileSize;
        song.Format = format;

        var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
        if (!decodeResult.IsSuccess || decodeResult.Value is null)
        {
            return song;
        }

        var samples = decodeResult.Value;
        song.DurationMs = (int)(samples.Length / (double)_featureOptions.TargetSampleRate * 1000);

        var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
        if (acousticResult.IsSuccess && acousticResult.Value is not null)
        {
            song.AcousticVector = acousticResult.Value;
            song.AcousticVectorBlob = _vectorSerializer.Serialize(acousticResult.Value);
            song.AcousticDim = acousticResult.Value.Length;
        }

        if (_deepExtractor.IsModelLoaded)
        {
            var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
            if (deepResult.IsSuccess && deepResult.Value is not null)
            {
                song.DeepVector = deepResult.Value;
                song.DeepVectorBlob = _vectorSerializer.Serialize(deepResult.Value);
                song.DeepModelType = _deepExtractor.ModelType.ToString();
                song.DeepDim = deepResult.Value.Length;
            }
            else
            {
                song.DeepVector = null;
                song.DeepVectorBlob = null;
                song.DeepModelType = null;
                song.DeepDim = null;
            }
        }

        if (song.AcousticVectorBlob is not null || song.DeepVectorBlob is not null)
        {
            song.FeatureExtractedAt = DateTime.UtcNow;
        }

        return song;
    }

    private async Task<Song> SupplementDeepVectorAsync(
        Song song, string md5, long fileSize, string format, CancellationToken ct)
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

            song.DeepVector = deepResult.Value;
            song.DeepVectorBlob = _vectorSerializer.Serialize(deepResult.Value);
            song.DeepModelType = _deepExtractor.ModelType.ToString();
            song.DeepDim = deepResult.Value.Length;
            song.FileMd5 = md5;
            song.FileSize = fileSize;
            song.Format = format;
            song.FeatureExtractedAt = DateTime.UtcNow;
            if (song.DurationMs is null)
            {
                song.DurationMs = (int)(samples.Length / (double)_featureOptions.TargetSampleRate * 1000);
            }

            var updateResult = await _songRepository.UpdateFeaturesAsync(song);
            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("更新深度向量到数据库失败: {FilePath}, {Error}", song.FilePath, updateResult.Error);
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
    private static SongDto MapToDto(Song song) => new()
    {
        Id = song.Id,
        FilePath = song.FilePath,
        Title = song.Title,
        Artist = song.Artist,
        IsLiked = song.IsLiked,
        HasFeatures = song.AcousticVectorBlob is not null
    };

    /// <inheritdoc/>
    public async Task<Result<SongDetailDto>> GetSongDetailAsync(int songId)
    {
        var songResult = await _songRepository.GetByIdAsync(songId);
        if (!songResult.IsSuccess)
        {
            return Result<SongDetailDto>.Failure(songResult.Error!, songResult.Exception);
        }

        var song = songResult.Value!;
        var tagResult = _audioTagService.ReadTags(song.FilePath);
        if (!tagResult.IsSuccess || tagResult.Value is null)
        {
            return Result<SongDetailDto>.Success(new SongDetailDto
            {
                Id = song.Id,
                FilePath = song.FilePath,
                Title = song.Title,
                Artist = song.Artist,
                Album = song.Album,
                AlbumArtist = song.AlbumArtist,
                Genre = song.Genre,
                Year = song.Year,
                Track = song.Track,
                Disc = song.Disc,
                Comment = song.Comment,
                Lyrics = song.Lyrics,
                Format = song.Format,
                FileSize = song.FileSize,
                DurationMs = song.DurationMs,
                FileMd5 = song.FileMd5,
                IsLiked = song.IsLiked,
                HasAcousticFeatures = song.AcousticVectorBlob is not null,
                HasDeepFeatures = song.DeepVectorBlob is not null,
                AcousticDim = song.AcousticDim,
                DeepDim = song.DeepDim,
                DeepModelType = song.DeepModelType,
                IsReadOnlyFile = true
            });
        }

        var detail = tagResult.Value;
        detail.Id = song.Id;
        detail.Format ??= song.Format;
        detail.FileSize ??= song.FileSize;
        detail.DurationMs ??= song.DurationMs;
        detail.FileMd5 = song.FileMd5;
        detail.IsLiked = song.IsLiked;
        detail.HasAcousticFeatures = song.AcousticVectorBlob is not null;
        detail.HasDeepFeatures = song.DeepVectorBlob is not null;
        detail.AcousticDim = song.AcousticDim;
        detail.DeepDim = song.DeepDim;
        detail.DeepModelType = song.DeepModelType;
        return Result<SongDetailDto>.Success(detail);
    }

    /// <inheritdoc/>
    public async Task<Result> SaveSongMetadataAsync(SongMetadataUpdateDto update)
    {
        var songResult = await _songRepository.GetByIdAsync(update.SongId);
        if (!songResult.IsSuccess)
        {
            return songResult;
        }

        var song = songResult.Value!;
        var writeResult = _audioTagService.WriteTags(song.FilePath, update);
        if (!writeResult.IsSuccess)
        {
            return writeResult;
        }

        var md5 = await FileContentHasher.ComputeMd5HexAsync(song.FilePath);
        var fileInfo = new FileInfo(song.FilePath);

        song.Title = update.Title;
        song.Artist = update.Artist;
        song.Album = update.Album;
        song.AlbumArtist = update.AlbumArtist;
        song.Genre = update.Genre;
        song.Year = update.Year;
        song.Track = update.Track;
        song.Disc = update.Disc;
        song.Comment = update.Comment;
        song.Lyrics = update.Lyrics;
        song.FileMd5 = md5;
        song.FileSize = fileInfo.Length;

        return await _songRepository.UpdateMetadataAsync(song);
    }

    private void ApplyTagsToSong(Song song)
    {
        try
        {
            var tagResult = _audioTagService.ReadTags(song.FilePath);
            if (tagResult is null || !tagResult.IsSuccess || tagResult.Value is null)
            {
                return;
            }

            var tag = tagResult.Value;
            if (!string.IsNullOrWhiteSpace(tag.Title))
            {
                song.Title = tag.Title;
            }

            song.Artist = tag.Artist ?? song.Artist;
            song.Album = tag.Album;
            song.AlbumArtist = tag.AlbumArtist;
            song.Genre = tag.Genre;
            song.Year = tag.Year;
            song.Track = tag.Track;
            song.Disc = tag.Disc;
            song.Comment = tag.Comment;
            song.Lyrics = tag.Lyrics;
            if (tag.DurationMs is > 0)
            {
                song.DurationMs = tag.DurationMs;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取标签跳过: {FilePath}", song.FilePath);
        }
    }
}
