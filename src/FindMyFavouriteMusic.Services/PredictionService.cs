using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
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
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly ILogger<PredictionService> _logger;

    /// <summary>
    /// 构造函数，通过 DI 注入所有依赖组件。
    /// </summary>
    /// <param name="audioDecoder">音频解码器</param>
    /// <param name="acousticExtractor">声学特征提取器</param>
    /// <param name="deepExtractor">深度特征提取器</param>
    /// <param name="predictionEngine">预测引擎，执行相似度计算与评分</param>
    /// <param name="profileRepository">用户画像仓储（直接注入避免循环依赖）</param>
    /// <param name="songRepository">歌曲仓储</param>
    /// <param name="vectorSerializer">向量序列化器</param>
    /// <param name="featureOptions">特征提取配置</param>
    /// <param name="logger">日志记录器</param>
    public PredictionService(
        IAudioDecoder audioDecoder,
        IAcousticFeatureExtractor acousticExtractor,
        IDeepFeatureExtractor deepExtractor,
        PredictionEngine predictionEngine,
        ProfileRepository profileRepository,
        ISongRepository songRepository,
        IVectorSerializer vectorSerializer,
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
        _featureOptions = featureOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// 基于文件路径预测用户偏好分数（适用于未入库的新文件）。
    /// </summary>
    /// <param name="filePath">音频文件绝对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预测结果，包含相似度分数与匹配详情</returns>
    /// <remarks>
    /// 端到端流程：
    /// 1. 获取用户画像并校验声学均值向量是否存在；
    /// 2. 解码音频文件得到采样数据；
    /// 3. 提取声学特征（必选）；
    /// 4. 若深度模型已加载，提取深度特征（可选，失败时静默降级）；
    /// 5. 反序列化画像中的均值向量；
    /// 6. 调用 PredictionEngine.Predict 完成最终打分。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(string filePath, CancellationToken ct = default)
    {
        // 步骤 1：获取用户画像
        var profileResult = await _profileRepository.GetAsync();
        if (!profileResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(profileResult.Error!, profileResult.Exception);
        }

        var profile = profileResult.Value;
        // 画像不存在或尚未构建（无喜欢歌曲）时返回友好错误，避免后续空引用
        if (profile?.AcousticMeanVectorBlob is null)
        {
            return Result<PredictionResult>.Failure("用户画像尚未构建，请先标记喜欢的歌曲");
        }

        // 步骤 2：解码音频文件
        var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
        if (!decodeResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(decodeResult.Error!, decodeResult.Exception);
        }

        // 步骤 3：提取声学特征（必选，作为相似度计算的基础）
        var samples = decodeResult.Value!;
        var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
        if (!acousticResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(acousticResult.Error!, acousticResult.Exception);
        }

        // 步骤 4：深度特征提取（可选），失败时静默降级为仅声学模式
        float[]? deepVector = null;
        if (_deepExtractor.IsModelLoaded)
        {
            var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
            // 不强制要求成功：deepVector 保持 null 时 PredictionEngine 会自动走仅声学模式
            if (deepResult.IsSuccess)
            {
                deepVector = deepResult.Value;
            }
        }

        // 步骤 5：反序列化画像均值向量（BLOB → float[]）
        var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob);
        float[]? profileDeep = profile.DeepMeanVectorBlob is not null
            ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob)
            : null;

        // 步骤 6：调用预测引擎计算相似度分数
        var prediction = _predictionEngine.Predict(
            acousticResult.Value!, deepVector, profileAcoustic, profileDeep);

        // 回填歌曲标题，便于 UI 直接展示
        if (prediction.IsSuccess && prediction.Value is not null)
        {
            prediction.Value.SongTitle = Path.GetFileNameWithoutExtension(filePath);
        }

        return prediction;
    }

    /// <summary>
    /// 基于歌曲 ID 预测用户偏好分数（优先复用已存储特征向量）。
    /// </summary>
    /// <param name="songId">歌曲 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预测结果</returns>
    /// <remarks>
    /// 优化策略：若该歌曲在入库时已提取并存储特征向量，则直接反序列化使用，
    /// 省略昂贵的解码与特征提取步骤；若无存储特征，则回退到 PredictAsync(filePath)。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(int songId, CancellationToken ct = default)
    {
        // 先查询歌曲实体，判断是否已存储特征向量
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
            if (!profileResult.IsSuccess || profileResult.Value?.AcousticMeanVector is null)
            {
                return Result<PredictionResult>.Failure("用户画像尚未构建");
            }

            var profile = profileResult.Value;
            // 反序列化歌曲与画像的向量
            var acousticVector = _vectorSerializer.Deserialize(song.AcousticVectorBlob);
            var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob!);
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

        // 回退路径：无存储特征，按文件路径走完整解码流程
        return await PredictAsync(song.FilePath, ct);
    }
}
