using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Results;

namespace FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 余弦相似度计算器
/// </summary>
public class CosineSimilarityCalculator : ISimilarityCalculator
{
    /// <inheritdoc/>
    public Result<double> Calculate(float[] vectorA, float[] vectorB)
    {
        ArgumentNullException.ThrowIfNull(vectorA);
        ArgumentNullException.ThrowIfNull(vectorB);

        if (vectorA.Length != vectorB.Length)
        {
            return Result<double>.Failure(
                $"向量维度不匹配: {vectorA.Length} vs {vectorB.Length}");
        }

        if (vectorA.Length == 0)
        {
            return Result<double>.Failure("向量不能为空");
        }

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        // 零向量时返回 0 避免除零
        if (normA == 0 || normB == 0)
        {
            return Result<double>.Success(0.0);
        }

        var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Result<double>.Success(similarity);
    }
}
