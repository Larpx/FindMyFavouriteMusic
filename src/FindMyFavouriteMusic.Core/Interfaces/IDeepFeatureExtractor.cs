using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 深度特征提取接口，支持优雅降级
/// </summary>
public interface IDeepFeatureExtractor
{
    /// <summary>模型是否已加载可用</summary>
    bool IsModelLoaded { get; }

    /// <summary>提取深度特征向量</summary>
    Task<Result<float[]>> ExtractAsync(float[] samples, int sampleRate, CancellationToken ct = default);

    /// <summary>提取的向量维度数</summary>
    int FeatureDimension { get; }

    /// <summary>
    /// 尝试加载 ONNX 模型
    /// </summary>
    /// <param name="modelPath">模型文件路径</param>
    /// <param name="modelType">模型类型（VGGish / MERT），工厂据此切换内部提取器</param>
    Result LoadModel(string modelPath, DeepModelType modelType);
}
