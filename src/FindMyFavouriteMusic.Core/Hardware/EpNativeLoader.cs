using System.IO;
using System.Threading;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// ONNX Runtime native 库加载器：启动时把 OpenVINO EP 的 native 库复制到输出根目录。
/// </summary>
/// <remarks>
/// <para><b>背景：</b>v2.0 起移除 DirectML EP，仅保留 OpenVINO + CPU 双 EP 架构。
/// OpenVINO native 包（<c>Intel.ML.OnnxRuntime.OpenVino</c>）包含完整 CPU EP 与 OpenVINO EP 的
/// native 库（onnxruntime.dll 及依赖），构建时由 MSBuild Target 复制到 <c>ep-openvino/</c> 子目录，
/// 运行时由本加载器把该子目录的 native 库复制到输出根目录后再加载 ORT。</para>
/// <para><b>调用时机：</b>必须在任何 ONNX Runtime API 调用之前调用 <see cref="Initialize"/>，
/// 否则进程已加载的 onnxruntime.dll 无法替换。建议在程序入口（如 App.OnFrameworkInitializationCompleted
/// 或测试程序集的 ModuleInitializer）的第一行调用。</para>
/// <para><b>幂等性：</b>多次调用安全，仅首次调用实际执行复制操作。</para>
/// <para><b>CPU 模式：</b>同样需要 native onnxruntime.dll（CPU EP 在 native 层实现），
/// 从 <c>ep-openvino/</c> 子目录复制（OpenVINO 包含完整 CPU EP）。</para>
/// </remarks>
public static class EpNativeLoader
{
    /// <summary>OpenVINO EP native 库子目录名</summary>
    private const string EpOpenVinoSubDir = "ep-openvino";

    /// <summary>初始化状态标志：0=未初始化，1=已初始化</summary>
    private static int _initialized;

    /// <summary>
    /// 把 <c>ep-openvino/</c> 子目录的 native 库复制到输出根目录。
    /// </summary>
    /// <param name="baseDir">输出根目录（通常是 <see cref="AppContext.BaseDirectory"/>）</param>
    /// <param name="ep">目标 EP 模式（仅用于日志，CPU 与 OpenVINO 都从同一子目录复制）</param>
    /// <remarks>
    /// <para>调用此方法前，确保 <paramref name="baseDir"/> 下存在 <c>ep-openvino/</c> 子目录
    /// （由 MSBuild Target 在构建时复制）。</para>
    /// <para>若子目录不存在（如未构建 EP 包），方法静默返回，不抛异常；
    /// 后续 ORT 加载可能失败，由调用方处理。</para>
    /// </remarks>
    public static void Initialize(string baseDir, ExecutionProviderMode ep)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        // CPU 与 OpenVINO 模式都从 ep-openvino 子目录复制
        // （OpenVINO 包的 onnxruntime.dll 也包含完整 CPU EP，未调用 AppendExecutionProvider_OpenVINO 时即纯 CPU 推理）
        var srcDir = Path.Combine(baseDir, EpOpenVinoSubDir);

        if (!Directory.Exists(srcDir))
        {
            return;
        }

        foreach (var srcFile in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(srcDir, srcFile);
            var destFile = Path.Combine(baseDir, relPath);
            var destDir = Path.GetDirectoryName(destFile);
            if (destDir != null)
            {
                Directory.CreateDirectory(destDir);
            }
            File.Copy(srcFile, destFile, overwrite: true);
        }
    }

    /// <summary>
    /// 重置初始化状态（仅用于测试场景，生产代码不应调用）。
    /// </summary>
    /// <remarks>
    /// 此方法仅重置内部标志，不会卸载已加载的 native 库（.NET 不支持卸载已加载的 native 库）。
    /// 测试中如需切换 EP，必须在不同进程中运行。
    /// </remarks>
    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _initialized, 0);
    }
}
