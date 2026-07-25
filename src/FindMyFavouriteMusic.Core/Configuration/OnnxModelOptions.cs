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
    /// 是否优先使用 NPU/GPU 加速推理。
    /// </summary>
    /// <remarks>
    /// <para>向后兼容字段：</para>
    /// <para>- 设为 false 时强制使用 CPU EP（覆盖 <see cref="ExecutionProvider"/>）；</para>
    /// <para>- 设为 true 时按 <see cref="ExecutionProvider"/> 字段选择具体 EP（DirectML / OpenVINO）。</para>
    /// <para>默认 true：保持旧行为兼容。</para>
    /// </remarks>
    public bool PreferNpu { get; set; } = true;

    /// <summary>
    /// 深度模型推理使用的 Execution Provider 类型。
    /// </summary>
    /// <remarks>
    /// <para>默认 <see cref="ExecutionProviderMode.DirectML"/>：保持与旧版本相同的行为；</para>
    /// <para>设为 <see cref="ExecutionProviderMode.OpenVINO"/> 时使用 Intel OpenVINO EP，
    /// 配合 <see cref="OpenVinoDevice"/> 指定目标设备（NPU/GPU/AUTO）；</para>
    /// <para>设为 <see cref="ExecutionProviderMode.CPU"/> 时强制使用 CPU EP。</para>
    /// <para>注意：当 <see cref="PreferNpu"/> = false 时，本字段被忽略，强制 CPU。</para>
    /// <para>注意：DirectML 与 OpenVINO 的 native 库互斥，启动时由
    /// <c>EpNativeLoader</c> 根据本字段复制对应 native 库到输出根目录。</para>
    /// </remarks>
    public ExecutionProviderMode ExecutionProvider { get; set; } = ExecutionProviderMode.DirectML;

    /// <summary>
    /// OpenVINO EP 的目标设备类型，仅当 <see cref="ExecutionProvider"/> = OpenVINO 时生效。
    /// </summary>
    /// <remarks>
    /// <para><see cref="OpenVinoDeviceType.NPU"/>：Intel AI Boost NPU（默认，符合 README TODO 初衷）；</para>
    /// <para><see cref="OpenVinoDeviceType.GPU"/>：Intel Arc 集成 GPU；</para>
    /// <para><see cref="OpenVinoDeviceType.AUTO"/>：OpenVINO 运行时自动选择最佳设备。</para>
    /// </remarks>
    public OpenVinoDeviceType OpenVinoDevice { get; set; } = OpenVinoDeviceType.NPU;

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
/// <para>DirectML 与 OpenVINO 的 native 库（onnxruntime.dll）物理互斥，
/// 启动时由 <c>EpNativeLoader</c> 根据配置复制对应 native 库到输出根目录，
/// 实现运行时通过配置文件切换 EP，无需重新编译。</para>
/// </remarks>
public enum ExecutionProviderMode
{
    /// <summary>强制使用 CPU EP，不加载任何加速 native 库</summary>
    CPU,

    /// <summary>使用 DirectML EP（Windows 通用加速方案，通过 DirectML 12 自动 offload 到 NPU/GPU）</summary>
    DirectML,

    /// <summary>使用 Intel OpenVINO EP（Intel 官方最优方案，配合 <see cref="OnnxModelOptions.OpenVinoDevice"/> 指定目标设备）</summary>
    OpenVINO
}

/// <summary>
/// OpenVINO EP 目标设备类型，仅当 <see cref="OnnxModelOptions.ExecutionProvider"/> = OpenVINO 时生效。
/// </summary>
public enum OpenVinoDeviceType
{
    /// <summary>Intel Neural Processing Unit（如 Intel AI Boost NPU）</summary>
    NPU,

    /// <summary>Intel 集成或独立 GPU</summary>
    GPU,

    /// <summary>OpenVINO 运行时自动选择最佳设备</summary>
    AUTO
}
