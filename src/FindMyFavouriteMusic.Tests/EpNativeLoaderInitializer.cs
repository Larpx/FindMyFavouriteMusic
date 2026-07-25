using System;
using System.Runtime.CompilerServices;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests;

/// <summary>
/// 测试程序集模块初始化器：在任何测试用例执行前（且在任何 ONNX Runtime P/Invoke 之前）
/// 根据环境变量调用 <see cref="EpNativeLoader.Initialize"/>，把对应 EP 的 native 库复制到输出根目录。
/// </summary>
/// <remarks>
/// <para><b>背景：</b>测试项目构建后，DirectML 与 OpenVINO 的 native 库分别位于
/// <c>ep-dml/</c> 与 <c>ep-openvino/</c> 子目录，根目录默认无 <c>onnxruntime.dll</c>。
/// 若不调用 <see cref="EpNativeLoader.Initialize"/>，ORT 会从系统目录
/// （如 <c>C:\Windows\System32\onnxruntime.dll</c>）加载一个不含任何 EP 的全局版本，
/// 导致 <c>AppendExecutionProvider_DML</c> / <c>AppendExecutionProvider_OpenVINO</c>
/// 在创建 <see cref="Microsoft.ML.OnnxRuntime.InferenceSession"/> 时失败。</para>
/// <para><b>环境变量：</b>复用生产配置键名（与 <c>App.axaml.cs</c> 中的 <c>InitializeEpNativeLib</c> 一致）：</para>
/// <para>- <c>FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider</c>：CPU / DirectML / OpenVINO（不区分大小写）；</para>
/// <para>- <c>FINDMYFAVOURITEMUSIC_OnnxModel__PreferNpu</c>：true / false，false 时强制 CPU（与生产逻辑保持一致）。</para>
/// <para><b>使用示例：</b></para>
/// <para><c>$env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider = "OpenVINO"; dotnet test</c></para>
/// <para><b>注意：</b>同一进程只能初始化一次（<see cref="EpNativeLoader"/> 内部幂等），
/// 且 native 库加载后无法卸载，因此单次 <c>dotnet test</c> 运行只能测试一种 EP。
/// 如需对比三种 EP，请分别运行三次 <c>dotnet test</c>。</para>
/// </remarks>
internal static class EpNativeLoaderInitializer
{
    /// <summary>
    /// 模块初始化入口：CLR 在加载测试程序集时自动调用，无需手动触发。
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var ep = ResolveExecutionProvider();
            EpNativeLoader.Initialize(AppContext.BaseDirectory, ep);
            Console.WriteLine($"[EpNativeLoaderInitializer] 已初始化 EP native 库: {ep}");
        }
        catch (Exception ex)
        {
            // 初始化失败不阻断测试启动，后续 ORT 加载失败由测试用例处理
            Console.Error.WriteLine($"[EpNativeLoaderInitializer] 初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取环境变量解析目标 EP 模式，与 <c>App.InitializeEpNativeLib</c> 决策逻辑保持一致。
    /// </summary>
    /// <returns>解析得到的 EP 模式；解析失败返回 <see cref="ExecutionProviderMode.DirectML"/>（生产默认值）。</returns>
    private static ExecutionProviderMode ResolveExecutionProvider()
    {
        // PreferNpu=false 时强制 CPU（与生产逻辑保持一致）
        var preferNpuRaw = Environment.GetEnvironmentVariable("FINDMYFAVOURITEMUSIC_OnnxModel__PreferNpu");
        if (bool.TryParse(preferNpuRaw, out var preferNpu) && !preferNpu)
        {
            return ExecutionProviderMode.CPU;
        }

        var epRaw = Environment.GetEnvironmentVariable("FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider");
        return epRaw?.Trim().ToLowerInvariant() switch
        {
            "cpu" => ExecutionProviderMode.CPU,
            "directml" or "dml" => ExecutionProviderMode.DirectML,
            "openvino" or "ov" => ExecutionProviderMode.OpenVINO,
            _ => ExecutionProviderMode.DirectML // 生产默认值
        };
    }
}
