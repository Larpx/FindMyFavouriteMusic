namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// 预测权重配置
/// </summary>
public class PredictionOptions
{
    public const string SectionName = "Prediction";

    /// <summary>声学特征相似度权重</summary>
    public double AcousticWeight { get; set; } = 0.4;

    /// <summary>深度特征相似度权重</summary>
    public double DeepWeight { get; set; } = 0.6;

    /// <summary>仅声学模式下的声学权重（无 ONNX 模型时）</summary>
    public double AcousticOnlyWeight { get; set; } = 1.0;
}
