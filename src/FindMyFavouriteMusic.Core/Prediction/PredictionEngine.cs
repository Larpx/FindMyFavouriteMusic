using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 预测引擎，根据声学与深度特征相似度计算加权评分。
/// </summary>
/// <remarks>
/// <para><b>职责：</b>仅负责"评分计算"这一单一职责，不直接依赖 ProfileService，
/// 画像数据由调用方注入，便于测试与复用。</para>
/// <para><b>评分公式（声学+深度模式）：</b></para>
/// <para>score = AcousticWeight × acousticScore + DeepWeight × deepScore</para>
/// <para>默认权重 0.4 / 0.6，深度特征权重更高，因其基于 VGGish 语义嵌入，对噪声与编码差异更鲁棒。</para>
/// <para><b>仅声学模式（降级）：</b></para>
/// <para>score = AcousticOnlyWeight × acousticScore</para>
/// <para>AcousticOnlyWeight 通常为 1.0，避免在缺少深度特征时对分数进行额外缩放。</para>
/// <para><b>相似度映射：</b>余弦相似度 [-1,1] 经 (sim + 1) / 2 × 100 线性映射到 [0,100]。</para>
/// <para><b>降级策略：</b>当深度相似度计算失败时，回退到仅声学模式并返回 <see cref="PredictionMode.AcousticOnly"/>，
/// 调用方可据此感知实际使用的特征通道。</para>
/// </remarks>
public class PredictionEngine
{
    private readonly ISimilarityCalculator _similarityCalculator;
    private readonly IDeepFeatureExtractor _deepFeatureExtractor;
    private readonly PredictionOptions _options;
    private readonly ILogger<PredictionEngine> _logger;

    /// <summary>
    /// 构造预测引擎。
    /// </summary>
    /// <param name="similarityCalculator">相似度计算器（如余弦相似度）。</param>
    /// <param name="deepFeatureExtractor">深度特征提取器，用于判断模型是否可用。</param>
    /// <param name="options">预测权重配置，通过 IOptions 模式注入。</param>
    /// <param name="logger">日志记录器。</param>
    public PredictionEngine(
        ISimilarityCalculator similarityCalculator,
        IDeepFeatureExtractor deepFeatureExtractor,
        IOptions<PredictionOptions> options,
        ILogger<PredictionEngine> logger)
    {
        _similarityCalculator = similarityCalculator;
        _deepFeatureExtractor = deepFeatureExtractor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 计算待预测歌曲相对用户画像的预测评分。
    /// </summary>
    /// <param name="acousticVector">待预测歌曲的声学特征向量。</param>
    /// <param name="deepVector">待预测歌曲的深度特征向量（可能为 null）。</param>
    /// <param name="profileAcousticVector">用户画像的声学均值向量。</param>
    /// <param name="profileDeepVector">用户画像的深度均值向量（可能为 null）。</param>
    /// <returns>携带总分、声学分、深度分（可选）与实际模式的 <see cref="PredictionResult"/>。</returns>
    /// <remarks>
    /// <para><b>useDeepFeatures 三个必要条件：</b></para>
    /// <para>1. 深度模型已加载（<see cref="IDeepFeatureExtractor.IsModelLoaded"/>）；</para>
    /// <para>2. 待预测歌曲存在深度向量（非 null）；</para>
    /// <para>3. 画像存在深度均值向量（非 null）。</para>
    /// <para>任一条件不满足，则进入仅声学模式，避免在缺失数据时强行计算深度相似度引发异常。</para>
    /// </remarks>
    public Result<PredictionResult> Predict(
        float[] acousticVector,
        float[]? deepVector,
        float[] profileAcousticVector,
        float[]? profileDeepVector)
    {
        // 计算声学特征相似度（声学通道总是可用，作为基础评分）
        var acousticResult = _similarityCalculator.Calculate(acousticVector, profileAcousticVector);
        if (!acousticResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(acousticResult.Error!, acousticResult.Exception);
        }

        // 映射到 0-100：将余弦相似度 [-1,1] 线性映射到 [0,100]
        var acousticScore = MapToScore(acousticResult.Value);

        // 三个条件全部满足时才启用深度特征：模型可用 + 待预测歌曲有深度向量 + 画像有深度均值向量
        var useDeepFeatures = _deepFeatureExtractor.IsModelLoaded
                              && deepVector is not null
                              && profileDeepVector is not null;

        if (useDeepFeatures)
        {
            // 计算深度特征相似度
            var deepResult = _similarityCalculator.Calculate(deepVector!, profileDeepVector!);
            if (!deepResult.IsSuccess)
            {
                // 降级策略：深度相似度计算失败时回退到仅声学模式，保证调用方仍能拿到可用评分
                _logger.LogWarning("深度特征相似度计算失败: {Error}，降级为仅声学模式", deepResult.Error);
                return Result<PredictionResult>.Success(new PredictionResult
                {
                    Score = acousticScore,
                    AcousticScore = acousticScore,
                    Mode = PredictionMode.AcousticOnly
                });
            }

            // 深度相似度映射到 [0,100]
            var deepScore = MapToScore(deepResult.Value);
            // 加权评分：声学分 × 0.4 + 深度分 × 0.6（深度权重更高，因其语义更鲁棒）
            var totalScore = _options.AcousticWeight * acousticScore
                           + _options.DeepWeight * deepScore;

            return Result<PredictionResult>.Success(new PredictionResult
            {
                // Math.Clamp 防止权重配置不当（如和不为 1）导致总分越出 [0,100]
                Score = Math.Clamp(totalScore, 0, 100),
                AcousticScore = acousticScore,
                DeepScore = deepScore,
                Mode = PredictionMode.AcousticAndDeep
            });
        }

        // 仅声学模式：AcousticOnlyWeight 通常为 1.0，避免对声学分进行额外缩放
        var score = _options.AcousticOnlyWeight * acousticScore;
        return Result<PredictionResult>.Success(new PredictionResult
        {
            // 同样使用 Clamp 防止权重异常导致越界
            Score = Math.Clamp(score, 0, 100),
            AcousticScore = acousticScore,
            Mode = PredictionMode.AcousticOnly
        });
    }

    /// <summary>
    /// 将余弦相似度 [-1, 1] 线性映射到 [0, 100] 评分区间。
    /// </summary>
    /// <param name="similarity">余弦相似度，范围 [-1, 1]。</param>
    /// <returns>映射后的评分，范围 [0, 100]。</returns>
    /// <remarks>
    /// <para>映射公式：score = (similarity + 1) / 2 × 100</para>
    /// <para>当 similarity = -1（完全相反）时得 0 分；</para>
    /// <para>当 similarity = 0（正交/无关）时得 50 分；</para>
    /// <para>当 similarity = 1（完全一致）时得 100 分。</para>
    /// <para>该线性映射保留了相似度的相对关系，便于后续加权与跨通道比较。</para>
    /// </remarks>
    private static double MapToScore(double similarity)
    {
        return (similarity + 1.0) / 2.0 * 100.0;
    }
}
