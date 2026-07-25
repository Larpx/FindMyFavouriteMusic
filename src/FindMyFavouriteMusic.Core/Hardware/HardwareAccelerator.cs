using System.Collections.Generic;
using System.Management;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// 硬件加速器默认实现：启动时通过 WMI 检测 Intel AI Boost NPU，
/// 并提供 OpenVINO / CPU 两种 Execution Provider 配置能力。
/// </summary>
/// <remarks>
/// <para><b>NPU 检测策略：</b>通过 WMI 查询 <c>Win32_PnPEntity</c>，
/// 代码层精确匹配设备名含 <c>Intel(R) AI Boost</c> 的设备。</para>
/// <para><b>EP 配置策略：</b>由 <see cref="OnnxModelOptions.ExecutionProvider"/> 字段决定：</para>
/// <para>- <see cref="ExecutionProviderMode.OpenVINO"/>：调用 <see cref="SessionOptions.AppendExecutionProvider_OpenVINO(string)"/> 注册 OpenVINO EP，
/// 目标设备由 <see cref="OnnxModelOptions.OpenVinoDevice"/> 指定（GPU/NPU/AUTO，默认 GPU）；</para>
/// <para>- <see cref="ExecutionProviderMode.CPU"/>：不附加任何 EP，使用纯 CPU 推理。</para>
/// <para><b>OpenVINO EP 优势：</b>Intel 官方为 Core Ultra NPU/GPU 提供的最优 EP，算子覆盖率与性能均优于 DirectML。
/// v2.0 起移除 DirectML EP（测试表明对 VGGish 比 CPU 慢，对 MERT 触发 CPU 回退）。</para>
/// <para><b>优雅降级：</b>WMI 查询失败、EP 注册失败均不抛异常，返回 Failure 由调用方回退 CPU。</para>
/// </remarks>
public class HardwareAccelerator : IHardwareAccelerator
{
    private readonly ILogger<HardwareAccelerator> _logger;
    private readonly ExecutionProviderMode _executionProvider;
    private readonly OpenVinoDeviceType _openVinoDevice;
    private readonly string? _openVinoCacheDir;

    /// <inheritdoc/>
    public bool IsNpuAvailable { get; }

    /// <inheritdoc/>
    public string? NpuDeviceName { get; }

    /// <inheritdoc/>
    public string ActiveExecutionProvider { get; private set; } = "CPU";

    /// <summary>
    /// 构造硬件加速器，启动时执行一次性 NPU 检测。
    /// </summary>
    /// <param name="options">ONNX 模型配置，读取 <see cref="OnnxModelOptions.ExecutionProvider"/>、
    /// <see cref="OnnxModelOptions.OpenVinoDevice"/>、<see cref="OnnxModelOptions.OpenVinoCacheDir"/></param>
    /// <param name="logger">日志记录器</param>
    public HardwareAccelerator(
        IOptions<OnnxModelOptions> options,
        ILogger<HardwareAccelerator> logger)
    {
        _logger = logger;
        _executionProvider = options.Value.ExecutionProvider;
        _openVinoDevice = options.Value.OpenVinoDevice;
        _openVinoCacheDir = options.Value.OpenVinoCacheDir;

        (IsNpuAvailable, NpuDeviceName) = DetectNpu();

        logger.LogInformation(
            "NPU 检测结果: Available={Available}, Device={Device}, ExecutionProvider={Ep}, OpenVinoDevice={OvDevice}",
            IsNpuAvailable, NpuDeviceName ?? "(未检测到)", _executionProvider, _openVinoDevice);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>决策流程：</para>
    /// <para>1. <see cref="OnnxModelOptions.ExecutionProvider"/> = CPU
    /// → 返回 Failure，调用方使用 CPU EP；</para>
    /// <para>2. ExecutionProvider = OpenVINO → 尝试追加 OpenVINO EP（按 <see cref="OpenVinoDeviceType"/> 指定设备），成功返回 Success；</para>
    /// <para>3. EP 注册异常 → 返回 Failure，调用方回退 CPU。</para>
    /// </remarks>
    public Result ConfigureSessionOptions(SessionOptions options)
    {
        if (_executionProvider == ExecutionProviderMode.CPU)
        {
            ActiveExecutionProvider = "CPU";
            return Result.Failure("已配置为 CPU EP");
        }

        try
        {
            switch (_executionProvider)
            {
                case ExecutionProviderMode.OpenVINO:
                    ConfigureOpenVinoEp(options);
                    return Result.Success();

                default:
                    ActiveExecutionProvider = "CPU";
                    return Result.Failure($"未支持的 EP 模式: {_executionProvider}");
            }
        }
        catch (Exception ex)
        {
            // EP 注册失败可能源于 native 库未加载、驱动缺失、设备不可用等
            ActiveExecutionProvider = "CPU";
            _logger.LogWarning(ex, "{Ep} EP 注册失败，将回退到 CPU EP", _executionProvider);
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 配置 OpenVINO EP，根据 <see cref="_openVinoDevice"/> 指定目标设备，可选启用编译缓存。
    /// </summary>
    /// <param name="options">待配置的会话选项</param>
    /// <remarks>
    /// <para>使用 ORT 1.22 的字符串重载 <see cref="SessionOptions.AppendExecutionProvider_OpenVINO(string)"/>
    /// 指定 device_type（GPU/NPU/AUTO）。</para>
    /// <para>编译缓存（cache_dir）通过 <see cref="SessionOptions.AddSessionConfigEntry"/> 传入，
    /// 对应 OpenVINO EP 的 <c>session.openvino.cache_dir</c> 配置项。</para>
    /// <para>由于 native 库切换由 <see cref="EpNativeLoader"/> 在启动时完成，此处假设 OpenVINO native 库已就位。</para>
    /// </remarks>
    private void ConfigureOpenVinoEp(SessionOptions options)
    {
        var deviceType = _openVinoDevice switch
        {
            OpenVinoDeviceType.GPU => "GPU",
            OpenVinoDeviceType.NPU => "NPU",
            OpenVinoDeviceType.AUTO => "AUTO",
            _ => "GPU"
        };

        if (!string.IsNullOrWhiteSpace(_openVinoCacheDir))
        {
            // OpenVINO EP 编译缓存：二次启动时复用已编译的模型，加速启动
            options.AddSessionConfigEntry("session.openvino.cache_dir", _openVinoCacheDir);
            _logger.LogInformation("OpenVINO 编译缓存目录: {CacheDir}", _openVinoCacheDir);
        }

        options.AppendExecutionProvider_OpenVINO(deviceType);
        ActiveExecutionProvider = $"OpenVINO({deviceType})";
        _logger.LogInformation("已启用 OpenVINO EP（device={Device}）进行推理加速", deviceType);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>提取器在推理失败后用 CPU EP 重建会话时调用此方法。</para>
    /// <para>幂等：若当前已是 CPU EP，不重复记录日志。</para>
    /// </remarks>
    public void MarkCpuFallbackActive()
    {
        if (ActiveExecutionProvider != "CPU")
        {
            ActiveExecutionProvider = "CPU";
            _logger.LogWarning("已回退到 CPU EP（推理失败触发的自动降级）");
        }
    }

    /// <summary>
    /// 通过 WMI 查询 NPU 设备，并在代码层精确过滤以排除误报。
    /// </summary>
    /// <returns>检测到 NPU 时返回 (true, 设备名)；否则返回 (false, null)。</returns>
    /// <remarks>
    /// <para>WMI 服务不可用或查询失败时返回 (false, null)，不抛异常。</para>
    /// <para><b>关键词校准说明（WMI LIKE 括号 bug 规避）：</b></para>
    /// <para>早期版本使用宽泛的 <c>%AI Boost%</c> 关键词，会误匹配 "Microsoft Input Configuration Device" 等 HID 设备。</para>
    /// <para>收紧为 <c>%Intel(R) AI Boost%</c> 后发现 WMI WQL 的 <c>LIKE</c> 运算符对括号 <c>()</c> 解析异常，
    /// <c>Name LIKE '%Intel(R) AI Boost%'</c> 仍会误匹配 HID 设备（实测于 Intel Core Ultra 7 155H）。</para>
    /// <para>最终方案：WMI 用宽泛 <c>%AI Boost%</c> 查询候选，代码层用
    /// <see cref="string.Contains(string, StringComparison)"/> 精确匹配 <c>Intel(R) AI Boost</c>，
    /// 彻底排除 HID 类误报。</para>
    /// </remarks>
    private static (bool available, string? deviceName) DetectNpu()
    {
        try
        {
            // WMI LIKE 对括号 () 解析异常，无法用 '%Intel(R) AI Boost%' 精确查询
            // 改用宽泛 '%AI Boost%' 查询候选，代码层精确过滤
            const string query = "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%AI Boost%'";

            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // 代码层精确匹配 Intel 官方 NPU 设备名，排除 HID 等误报
                if (name.Contains("Intel(R) AI Boost", StringComparison.OrdinalIgnoreCase))
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
