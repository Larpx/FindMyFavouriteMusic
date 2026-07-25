namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// ONNX 模型配置，支持 VGGish 与 MERT 两种深度特征提取模型
/// </summary>
public class OnnxModelOptions
{
    public const string SectionName = "OnnxModel";

    /// <summary>
    /// 深度特征提取器类型：VGGish 或 MERT。
    /// </summary>
    /// <remarks>
    /// v2.0 起默认 <see cref="DeepModelType.MERT"/>（基于性能测试结论：
    /// MERT 768 维音乐专用特征推荐质量优于 VGGish 128 维通用特征，详见 <c>docs/算法说明.md</c> 第 10 章）。
    /// </remarks>
    public DeepModelType ModelType { get; set; } = DeepModelType.MERT;

    /// <summary>VGGish ONNX 模型文件路径（128 维输出）</summary>
    public string? VggishModelPath { get; set; }

    /// <summary>MERT ONNX 模型文件路径（768 维输出）</summary>
    public string? MertModelPath { get; set; }

    /// <summary>是否启用深度特征提取</summary>
    public bool EnableDeepFeatures { get; set; }

    /// <summary>
    /// 深度模型推理使用的 Execution Provider 类型。
    /// </summary>
    /// <remarks>
    /// <para>默认 <see cref="ExecutionProviderMode.OpenVINO"/>：基于性能测试结论，
    /// OpenVINO(GPU) 对 MERT 有 2.24x 加速，且 OpenVINO 包含完整 CPU EP，
    /// CPU 模式下也可直接使用同一 native 库。</para>
    /// <para>设为 <see cref="ExecutionProviderMode.OpenVINO"/> 时使用 Intel OpenVINO EP，
    /// 配合 <see cref="OpenVinoDevice"/> 指定目标设备（NPU/GPU/AUTO，默认 GPU）；</para>
    /// <para>设为 <see cref="ExecutionProviderMode.CPU"/> 时使用纯 CPU EP。</para>
    /// <para>注意：OpenVINO 的 native 库（onnxruntime.dll）由
    /// <c>EpNativeLoader</c> 在启动时从 <c>ep-openvino/</c> 子目录复制到输出根目录。</para>
    /// </remarks>
    public ExecutionProviderMode ExecutionProvider { get; set; } = ExecutionProviderMode.OpenVINO;

    /// <summary>
    /// OpenVINO EP 的目标设备类型，仅当 <see cref="ExecutionProvider"/> = OpenVINO 时生效。
    /// </summary>
    /// <remarks>
    /// <para><see cref="OpenVinoDeviceType.GPU"/>：Intel Arc 集成 GPU（默认，测试最优）；</para>
    /// <para><see cref="OpenVinoDeviceType.NPU"/>：Intel AI Boost NPU；</para>
    /// <para><see cref="OpenVinoDeviceType.AUTO"/>：OpenVINO 运行时自动选择最佳设备。</para>
    /// </remarks>
    public OpenVinoDeviceType OpenVinoDevice { get; set; } = OpenVinoDeviceType.GPU;

    /// <summary>
    /// OpenVINO 编译缓存目录（可选）。
    /// </summary>
    /// <remarks>
    /// <para>指定后，OpenVINO EP 会将模型编译结果缓存到该目录，二次启动加速；</para>
    /// <para>留空则不启用缓存。建议指定一个可写目录，如 <c>./openvino-cache</c>。</para>
    /// </remarks>
    public string? OpenVinoCacheDir { get; set; }
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

/// <summary>
/// ONNX Runtime Execution Provider 选择模式。
/// </summary>
/// <remarks>
/// <para>v2.0 起移除 DirectML EP（测试表明对 VGGish 比 CPU 慢，对 MERT 触发 CPU 回退），
/// 仅保留 OpenVINO + CPU 双 EP 架构。</para>
/// <para>OpenVINO 的 native 库（onnxruntime.dll）由 <c>EpNativeLoader</c> 在启动时
/// 从 <c>ep-openvino/</c> 子目录复制到输出根目录；CPU 模式同样使用该 native 库
/// （OpenVINO 包含完整 CPU EP）。</para>
/// </remarks>
public enum ExecutionProviderMode
{
    /// <summary>使用纯 CPU EP，不附加任何加速器</summary>
    CPU,

    /// <summary>使用 Intel OpenVINO EP，配合 <see cref="OnnxModelOptions.OpenVinoDevice"/> 指定目标设备</summary>
    OpenVINO
}

/// <summary>
/// OpenVINO EP 目标设备类型，仅当 <see cref="OnnxModelOptions.ExecutionProvider"/> = OpenVINO 时生效。
/// </summary>
public enum OpenVinoDeviceType
{
    /// <summary>Intel 集成或独立 GPU（测试最优，对 MERT 有 2.24x 加速）</summary>
    GPU,

    /// <summary>Intel Neural Processing Unit（如 Intel AI Boost NPU）</summary>
    NPU,

    /// <summary>OpenVINO 运行时自动选择最佳设备</summary>
    AUTO
}
