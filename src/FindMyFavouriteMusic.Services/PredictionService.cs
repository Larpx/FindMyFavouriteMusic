using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 预测服务，负责端到端编排：解码 → 特征提取 → 相似度计算 → 输出预测分数。
/// </summary>
/// <remarks>
/// 提供两个 PredictAsync 重载：
/// 1. PredictAsync(string filePath)：从文件解码并提取特征后预测，适用于新文件尚未入库的场景；
/// 2. PredictAsync(int songId)：优先复用数据库中已存储的特征向量，避免重复解码开销；
///    若该歌曲尚无特征向量，则回退到按文件路径预测的流程。
/// 设计考量：直接注入 ProfileRepository（而非 IProfileService）以避免循环依赖，
/// 且预测场景只需读取画像数据，无需触发画像更新逻辑。
/// </remarks>
public class PredictionService : IPredictionService
{
    private readonly IAudioDecoder _audioDecoder;
    private readonly IAcousticFeatureExtractor _acousticExtractor;
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly PredictionEngine _predictionEngine;
    // 直接注入 ProfileRepository 而非 IProfileService：避免循环依赖，且仅需读取画像
    private readonly ProfileRepository _profileRepository;
    private readonly ISongRepository _songRepository;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly IModelOperationLock _modelLock;
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly ILogger<PredictionService> _logger;

    /// <summary>
    /// 构造函数，通过 DI 注入所有依赖组件。
    /// </summary>
    public PredictionService(
        IAudioDecoder audioDecoder,
        IAcousticFeatureExtractor acousticExtractor,
        IDeepFeatureExtractor deepExtractor,
        PredictionEngine predictionEngine,
        ProfileRepository profileRepository,
        ISongRepository songRepository,
        IVectorSerializer vectorSerializer,
        IModelOperationLock modelLock,
        IOptions<FeatureExtractionOptions> featureOptions,
        ILogger<PredictionService> logger)
    {
        _audioDecoder = audioDecoder;
        _acousticExtractor = acousticExtractor;
        _deepExtractor = deepExtractor;
        _predictionEngine = predictionEngine;
        _profileRepository = profileRepository;
        _songRepository = songRepository;
        _vectorSerializer = vectorSerializer;
        _modelLock = modelLock;
        _featureOptions = featureOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(string filePath, CancellationToken ct = default)
    {
        await using var gate = await _modelLock.AcquireAsync(ct);
        return await PredictFromFileCoreAsync(filePath, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(int songId, CancellationToken ct = default)
    {
        await using var gate = await _modelLock.AcquireAsync(ct);

        var songResult = await _songRepository.GetByIdAsync(songId);
        if (!songResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(songResult.Error!, songResult.Exception);
        }

        var song = songResult.Value!;

        // 快速路径：已存储声学特征，直接复用，避免重复解码
        if (song.AcousticVectorBlob is not null)
        {
            var profileResult = await _profileRepository.GetAsync();
            // 仓储只填充 BLOB，必须检查 AcousticMeanVectorBlob（而非未填充的 float[]）
            if (!profileResult.IsSuccess || profileResult.Value?.AcousticMeanVectorBlob is null)
            {
                return Result<PredictionResult>.Failure("用户画像尚未构建");
            }

            var profile = profileResult.Value;
            var acousticVector = _vectorSerializer.Deserialize(song.AcousticVectorBlob);
            var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob);
            float[]? deepVector = song.DeepVectorBlob is not null
                ? _vectorSerializer.Deserialize(song.DeepVectorBlob) : null;
            float[]? profileDeep = profile.DeepMeanVectorBlob is not null
                ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob) : null;

            var prediction = _predictionEngine.Predict(acousticVector, deepVector, profileAcoustic, profileDeep);
            if (prediction.IsSuccess && prediction.Value is not null)
            {
                prediction.Value.SongTitle = song.Title ?? Path.GetFileNameWithoutExtension(song.FilePath);
            }
            return prediction;
        }

        // 回退路径：无存储特征；已持有锁，调用 Core 避免重入死锁
        return await PredictFromFileCoreAsync(song.FilePath, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictWithProgressAsync(
        string filePath, IProgress<int>? progress, CancellationToken ct = default)
    {
        await using var gate = await _modelLock.AcquireAsync(ct);
        return await PredictFromFileCoreAsync(filePath, progress, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Result<PredictionResult>>> PredictBatchAsync(
        IReadOnlyList<string> filePaths, IProgress<int>? progress, CancellationToken ct = default)
    {
        // 整批持锁，避免中途切模型
        await using var gate = await _modelLock.AcquireAsync(ct);

        var results = new Result<PredictionResult>[filePaths.Count];
        var completed = 0;

        for (var i = 0; i < filePaths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var startPercent = (int)Math.Floor((double)completed / filePaths.Count * 100);
            var endPercent = (int)Math.Floor((double)(completed + 1) / filePaths.Count * 100);

            var fileProgress = new Progress<int>(p =>
            {
                var overallPercent = startPercent + (int)Math.Round((double)p / 100 * (endPercent - startPercent));
                progress?.Report(overallPercent);
            });

            results[i] = await PredictFromFileCoreAsync(filePaths[i], fileProgress, ct);
            completed++;
            progress?.Report((int)Math.Floor((double)completed / filePaths.Count * 100));
        }

        return results;
    }

    /// <summary>
    /// 按文件路径预测的核心实现（调用方须已持有模型锁）。
    /// </summary>
    private async Task<Result<PredictionResult>> PredictFromFileCoreAsync(
        string filePath, IProgress<int>? progress, CancellationToken ct)
    {
        try
        {
            progress?.Report(0);

            var profileResult = await _profileRepository.GetAsync();
            if (!profileResult.IsSuccess)
            {
                return Result<PredictionResult>.Failure(profileResult.Error!, profileResult.Exception);
            }

            var profile = profileResult.Value;
            if (profile?.AcousticMeanVectorBlob is null)
            {
                return Result<PredictionResult>.Failure("用户画像尚未构建，请先标记喜欢的歌曲");
            }

            progress?.Report(5);

            var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
            if (!decodeResult.IsSuccess)
            {
                return Result<PredictionResult>.Failure(decodeResult.Error!, decodeResult.Exception);
            }

            progress?.Report(25);

            var samples = decodeResult.Value!;
            var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
            if (!acousticResult.IsSuccess)
            {
                return Result<PredictionResult>.Failure(acousticResult.Error!, acousticResult.Exception);
            }

            progress?.Report(50);

            float[]? deepVector = null;
            if (_deepExtractor.IsModelLoaded)
            {
                var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
                if (deepResult.IsSuccess)
                {
                    deepVector = deepResult.Value;
                }
                else
                {
                    _logger.LogWarning("深度特征提取失败，将降级为仅声学模式: {Error}", deepResult.Error);
                }
            }

            progress?.Report(75);

            var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob);
            float[]? profileDeep = profile.DeepMeanVectorBlob is not null
                ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob)
                : null;

            progress?.Report(90);

            var prediction = _predictionEngine.Predict(
                acousticResult.Value!, deepVector, profileAcoustic, profileDeep);

            if (prediction.IsSuccess && prediction.Value is not null)
            {
                prediction.Value.SongTitle = Path.GetFileNameWithoutExtension(filePath);
            }

            progress?.Report(100);
            return prediction;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "预测失败: {FilePath}", filePath);
            return Result<PredictionResult>.Failure(ex);
        }
    }
}
