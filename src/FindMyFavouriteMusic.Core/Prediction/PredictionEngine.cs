using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 预测引擎，计算加权评分
/// </summary>
public class PredictionEngine
{
    private readonly ISimilarityCalculator _similarityCalculator;
    private readonly IDeepFeatureExtractor _deepFeatureExtractor;
    private readonly PredictionOptions _options;
    private readonly ILogger<PredictionEngine> _logger;

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
    /// 计算预测评分
    /// </summary>
    /// <param name="acousticVector">待预测歌曲的声学特征向量</param>
    /// <param name="deepVector">待预测歌曲的深度特征向量（可能为 null）</param>
    /// <param name="profileAcousticVector">用户画像的声学均值向量</param>
    /// <param name="profileDeepVector">用户画像的深度均值向量（可能为 null）</param>
    public Result<PredictionResult> Predict(
        float[] acousticVector,
        float[]? deepVector,
        float[] profileAcousticVector,
        float[]? profileDeepVector)
    {
        // 计算声学特征相似度
        var acousticResult = _similarityCalculator.Calculate(acousticVector, profileAcousticVector);
        if (!acousticResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(acousticResult.Error!, acousticResult.Exception);
        }

        // 映射到 0-100
        var acousticScore = MapToScore(acousticResult.Value);
        var useDeepFeatures = _deepFeatureExtractor.IsModelLoaded
                              && deepVector is not null
                              && profileDeepVector is not null;

        if (useDeepFeatures)
        {
            var deepResult = _similarityCalculator.Calculate(deepVector!, profileDeepVector!);
            if (!deepResult.IsSuccess)
            {
                _logger.LogWarning("深度特征相似度计算失败: {Error}，降级为仅声学模式", deepResult.Error);
                return Result<PredictionResult>.Success(new PredictionResult
                {
                    Score = acousticScore,
                    AcousticScore = acousticScore,
                    Mode = PredictionMode.AcousticOnly
                });
            }

            var deepScore = MapToScore(deepResult.Value);
            var totalScore = _options.AcousticWeight * acousticScore
                           + _options.DeepWeight * deepScore;

            return Result<PredictionResult>.Success(new PredictionResult
            {
                Score = Math.Clamp(totalScore, 0, 100),
                AcousticScore = acousticScore,
                DeepScore = deepScore,
                Mode = PredictionMode.AcousticAndDeep
            });
        }

        // 仅声学模式
        var score = _options.AcousticOnlyWeight * acousticScore;
        return Result<PredictionResult>.Success(new PredictionResult
        {
            Score = Math.Clamp(score, 0, 100),
            AcousticScore = acousticScore,
            Mode = PredictionMode.AcousticOnly
        });
    }

    /// <summary>
    /// 将余弦相似度 [-1, 1] 映射到 [0, 100]
    /// </summary>
    private static double MapToScore(double similarity)
    {
        return (similarity + 1.0) / 2.0 * 100.0;
    }
}
