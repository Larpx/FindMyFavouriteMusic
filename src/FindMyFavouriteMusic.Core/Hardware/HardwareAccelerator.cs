using System.Management;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// 硬件加速器默认实现：启动时通过 WMI 检测 Intel AI Boost NPU，
/// 并提供 DirectML EP 配置能力。
/// </summary>
/// <remarks>
/// <para><b>NPU 检测策略：</b>通过 WMI 查询 <c>Win32_PnPEntity</c>，
/// 匹配设备名含 "NPU"、"Intel(R) AI Boost"、"Neural Processing"、"AI Boost" 的设备。</para>
/// <para><b>EP 配置策略：</b>当 <see cref="OnnxModelOptions.PreferNpu"/> 为 true 且检测到 NPU 时，
/// 调用 <see cref="SessionOptions.AppendExecutionProvider_DML(int)"/> 注册 DirectML EP（device 0）。</para>
/// <para><b>关于 DirectML 与 NPU 的关系：</b>DirectML 12 在 Windows 11 24H2+ 上会自动将 NPU 支持的算子
/// offload 到 NPU，由 DirectML 运行时与驱动协同决策。本实现不直接区分 NPU 与 GPU 设备索引，
/// 统一使用 device 0，让 DirectML 自行选择最佳执行设备。</para>
/// <para><b>优雅降级：</b>WMI 查询失败、DirectML EP 注册失败均不抛异常，返回 Failure 由调用方回退 CPU。</para>
/// </remarks>
public class HardwareAccelerator : IHardwareAccelerator
{
    private readonly ILogger<HardwareAccelerator> _logger;
    private readonly bool _preferNpu;

    /// <inheritdoc/>
    public bool IsNpuAvailable { get; }

    /// <inheritdoc/>
    public string? NpuDeviceName { get; }

    /// <inheritdoc/>
    public string ActiveExecutionProvider { get; private set; } = "CPU";

    /// <summary>
    /// 构造硬件加速器，启动时执行一次性 NPU 检测。
    /// </summary>
    /// <param name="options">ONNX 模型配置，读取 <see cref="OnnxModelOptions.PreferNpu"/></param>
    /// <param name="logger">日志记录器</param>
    public HardwareAccelerator(
        IOptions<OnnxModelOptions> options,
        ILogger<HardwareAccelerator> logger)
    {
        _logger = logger;
        _preferNpu = options.Value.PreferNpu;

        (IsNpuAvailable, NpuDeviceName) = DetectNpu();

        logger.LogInformation(
            "NPU 检测结果: Available={Available}, Device={Device}, PreferNpu={PreferNpu}",
            IsNpuAvailable, NpuDeviceName ?? "(未检测到)", _preferNpu);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>决策流程：</para>
    /// <para>1. 未检测到 NPU 或用户禁用 PreferNpu → 返回 Failure，调用方使用 CPU EP；</para>
    /// <para>2. 检测到 NPU 且启用 → 尝试追加 DirectML EP，成功返回 Success；</para>
    /// <para>3. DirectML 注册异常 → 返回 Failure，调用方回退 CPU。</para>
    /// </remarks>
    public Result ConfigureSessionOptions(SessionOptions options)
    {
        // 不满足启用条件时直接返回 Failure，调用方据此使用 CPU EP
        if (!IsNpuAvailable || !_preferNpu)
        {
            ActiveExecutionProvider = "CPU";
            return Result.Failure(IsNpuAvailable ? "用户已禁用 NPU 加速" : "未检测到 NPU");
        }

        try
        {
            // DirectML device 0：通常为主 GPU/NPU 适配器
            // 在 Windows 11 24H2+ 配合 DirectML 12 时，NPU 支持的算子会自动 offload 到 NPU
            options.AppendExecutionProvider_DML(0);
            ActiveExecutionProvider = "DirectML";
            _logger.LogInformation("已启用 DirectML EP（device 0）进行推理加速");
            return Result.Success();
        }
        catch (Exception ex)
        {
            // DirectML EP 注册失败可能源于驱动缺失、DirectML 运行时不可用等
            ActiveExecutionProvider = "CPU";
            _logger.LogWarning(ex, "DirectML EP 注册失败，将回退到 CPU EP");
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 通过 WMI 查询 NPU 设备，匹配关键词见类备注。
    /// </summary>
    /// <returns>检测到 NPU 时返回 (true, 设备名)；否则返回 (false, null)。</returns>
    /// <remarks>WMI 服务不可用或查询失败时返回 (false, null)，不抛异常。</remarks>
    private static (bool available, string? deviceName) DetectNpu()
    {
        try
        {
            // 关键词覆盖 Intel AI Boost（Meteor Lake+）、通用 NPU、Neural Processing 等命名
            const string query = "SELECT Name FROM Win32_PnPEntity WHERE " +
                "Name LIKE '%NPU%' OR " +
                "Name LIKE '%Intel(R) AI Boost%' OR " +
                "Name LIKE '%Neural Processing%' OR " +
                "Name LIKE '%AI Boost%'";

            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return (true, name);
                }
            }
        }
        catch
        {
            // WMI 查询失败（服务禁用/权限不足）视为未检测到 NPU
        }

        return (false, null);
    }
}
