using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 声学特征提取接口
/// </summary>
public interface IAcousticFeatureExtractor
{
    /// <summary>提取声学特征向量（聚合后的固定维度）</summary>
    Result<float[]> Extract(float[] samples, int sampleRate);

    /// <summary>提取的向量维度数</summary>
    int FeatureDimension { get; }
}
