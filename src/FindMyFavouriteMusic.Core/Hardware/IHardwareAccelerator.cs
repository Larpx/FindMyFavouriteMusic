using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.ML.OnnxRuntime;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// 硬件加速器抽象：负责 NPU/GPU 检测，并为 ONNX Runtime 推理会话配置 Execution Provider。
/// </summary>
/// <remarks>
/// <para>该接口将"硬件检测"与"EP 配置"从特征提取器中解耦，使提取器仅关注推理流水线本身。</para>
/// <para>典型用法：提取器在 <c>LoadModel</c> 时调用 <see cref="ConfigureSessionOptions"/>，
/// 根据返回结果决定使用 DirectML EP（成功）还是回退到 CPU EP（失败）。</para>
/// </remarks>
public interface IHardwareAccelerator
{
    /// <summary>是否检测到 NPU 设备</summary>
    bool IsNpuAvailable { get; }

    /// <summary>检测到的 NPU 设备名（未检测到时为 null）</summary>
    string? NpuDeviceName { get; }

    /// <summary>
    /// 当前实际生效的 Execution Provider 名称（如 "CPU"、"DirectML"）。
    /// </summary>
    /// <remarks>该值在每次 <see cref="ConfigureSessionOptions"/> 调用后更新，反映最近一次加载模型所用的 EP。</remarks>
    string ActiveExecutionProvider { get; }

    /// <summary>
    /// 根据检测结果与配置策略，为 ONNX Runtime 会话配置 Execution Provider。
    /// </summary>
    /// <param name="options">待配置的会话选项对象，方法内部会向其追加 EP。</param>
    /// <returns>成功表示已追加 DirectML EP（调用方应使用该 options 创建会话）；失败表示应回退到 CPU EP（使用默认 options 或不附加 EP）。</returns>
    /// <remarks>
    /// <para>调用方无需关心是 NPU 还是 GPU，本方法仅决定是否启用 DirectML EP。</para>
    /// <para>方法内部捕获 EP 注册异常并返回 Failure，确保调用方可以无异常地回退到 CPU。</para>
    /// </remarks>
    Result ConfigureSessionOptions(SessionOptions options);
}
