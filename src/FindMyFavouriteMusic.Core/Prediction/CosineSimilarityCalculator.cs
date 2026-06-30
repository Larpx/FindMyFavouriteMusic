using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;

/// <summary>
/// 余弦相似度计算器：衡量两个特征向量在方向上的接近程度。
/// </summary>
/// <remarks>
/// 余弦相似度公式：
/// <para>cos(A, B) = (A · B) / (||A|| × ||B||) = Σ(A[i]×B[i]) / (sqrt(Σ A[i]²) × sqrt(Σ B[i]²))</para>
/// <para>取值范围 [-1, 1]：</para>
/// <para>- 1 表示方向完全相同（最相似）</para>
/// <para>- 0 表示正交（不相关）</para>
/// <para>- -1 表示方向完全相反（最不相似）</para>
/// <para>选择余弦相似度而非欧氏距离的原因：</para>
/// <para>1. 余弦相似度对特征向量的绝对幅值不敏感，只关注方向；</para>
/// <para>2. 音频特征向量的幅值受音量、录音条件等无关因素影响，方向才是品味的本质；</para>
/// <para>3. 在高维空间（52 维声学 + 128 维深度）中，余弦相似度的判别力优于欧氏距离。</para>
/// </remarks>
public class CosineSimilarityCalculator : ISimilarityCalculator
{
    /// <inheritdoc/>
    public Result<double> Calculate(float[] vectorA, float[] vectorB)
    {
        ArgumentNullException.ThrowIfNull(vectorA);
        ArgumentNullException.ThrowIfNull(vectorB);

        // 维度必须一致
        if (vectorA.Length != vectorB.Length)
        {
            return Result<double>.Failure(
                $"向量维度不匹配: {vectorA.Length} vs {vectorB.Length}");
        }

        if (vectorA.Length == 0)
        {
            return Result<double>.Failure("向量不能为空");
        }

        // 累积点积与各自 L2 范数的平方
        double dotProduct = 0;  // A · B
        double normA = 0;       // ||A||²
        double normB = 0;       // ||B||²

        for (var i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        // 零向量时返回 0（避免除零），表示无相似性信息
        if (normA == 0 || normB == 0)
        {
            return Result<double>.Success(0.0);
        }

        // cos(A,B) = (A·B) / (||A|| × ||B||)
        var similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Result<double>.Success(similarity);
    }
}
