using FindMyFavouriteMusic.Models.Results;

namespace FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 相似度计算接口
/// </summary>
public interface ISimilarityCalculator
{
    /// <summary>计算两个向量的余弦相似度，返回 [-1, 1] 范围</summary>
    Result<double> Calculate(float[] vectorA, float[] vectorB);
}
