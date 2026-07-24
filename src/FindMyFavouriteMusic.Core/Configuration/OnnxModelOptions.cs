namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// ONNX 模型配置，支持 VGGish 与 MERT 两种深度特征提取模型
/// </summary>
public class OnnxModelOptions
{
    public const string SectionName = "OnnxModel";

    /// <summary>深度特征提取器类型：VGGish 或 MERT</summary>
    public DeepModelType ModelType { get; set; } = DeepModelType.VGGish;

    /// <summary>VGGish ONNX 模型文件路径（128 维输出）</summary>
    public string? VggishModelPath { get; set; }

    /// <summary>MERT ONNX 模型文件路径（768 维输出）</summary>
    public string? MertModelPath { get; set; }

    /// <summary>是否启用深度特征提取</summary>
    public bool EnableDeepFeatures { get; set; }

    /// <summary>
    /// 是否优先使用 NPU/GPU 加速推理（DirectML EP）。
    /// </summary>
    /// <remarks>
    /// <para>默认 true：启动时检测到 NPU 后自动尝试通过 DirectML EP 加载模型；</para>
    /// <para>设为 false 时强制使用 CPU EP，便于调试与对比性能。</para>
    /// <para>注意：实际是否使用 NPU 取决于运行时检测与 DirectML 加载是否成功，本开关仅控制是否尝试。</para>
    /// </remarks>
    public bool PreferNpu { get; set; } = true;
}

/// <summary>
/// 深度特征提取模型类型
/// </summary>
public enum DeepModelType
{
    /// <summary>VGGish 模型：Google Audioset 预训练，128 维输出，需输入 mel 频谱图</summary>
    VGGish,

    /// <summary>MERT 模型：音乐专用自监督模型，768 维输出，直接输入原始波形</summary>
    MERT
}
