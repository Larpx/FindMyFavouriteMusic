namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// ONNX 模型配置
/// </summary>
public class OnnxModelOptions
{
    public const string SectionName = "OnnxModel";

    /// <summary>VGGish ONNX 模型文件路径</summary>
    public string? VggishModelPath { get; set; }

    /// <summary>是否启用深度特征提取</summary>
    public bool EnableDeepFeatures { get; set; }
}
