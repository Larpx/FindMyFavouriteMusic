using System.IO;
using System.Threading;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// ONNX Runtime native 库加载器：启动时根据配置把对应 EP 的 native 库复制到输出根目录。
/// </summary>
/// <remarks>
/// <para><b>背景：</b>DirectML 与 OpenVINO EP 的 native 库（onnxruntime.dll）物理互斥，
/// 同一输出目录无法同时包含两个 EP 的 native 库。构建时由 MSBuild Target 把两个 EP 的
/// native 库分别复制到 <c>ep-dml/</c> 与 <c>ep-openvino/</c> 子目录，
/// 运行时由本加载器根据配置把对应子目录的 native 库复制到输出根目录后再加载 ORT。</para>
/// <para><b>调用时机：</b>必须在任何 ONNX Runtime API 调用之前调用 <see cref="Initialize"/>，
/// 否则进程已加载的 onnxruntime.dll 无法替换。建议在程序入口（如 App.OnFrameworkInitializationCompleted
/// 或测试程序集的 ModuleInitializer）的第一行调用。</para>
/// <para><b>幂等性：</b>多次调用安全，仅首次调用实际执行复制操作。</para>
/// <para><b>CPU 模式：</b>仍需 native onnxruntime.dll（CPU EP 在 native 层实现），
/// 默认从 <c>ep-dml/</c> 子目录复制（DirectML 包含完整 CPU EP）。</para>
/// </remarks>
public static class EpNativeLoader
{
    /// <summary>DirectML EP native 库子目录名</summary>
    private const string EpDmlSubDir = "ep-dml";

    /// <summary>OpenVINO EP native 库子目录名</summary>
    private const string EpOpenVinoSubDir = "ep-openvino";

    /// <summary>初始化状态标志：0=未初始化，1=已初始化</summary>
    private static int _initialized;

    /// <summary>
    /// 根据配置的 EP 模式，把对应子目录的 native 库复制到输出根目录。
    /// </summary>
    /// <param name="baseDir">输出根目录（通常是 <see cref="AppContext.BaseDirectory"/>）</param>
    /// <param name="ep">目标 EP 模式</param>
    /// <remarks>
    /// <para>调用此方法前，确保 <paramref name="baseDir"/> 下存在 <c>ep-dml/</c> 或 <c>ep-openvino/</c> 子目录
    /// （由 MSBuild Target 在构建时复制）。</para>
    /// <para>若对应子目录不存在（如未构建 EP 包），方法静默返回，不抛异常；
    /// 后续 ORT 加载可能失败，由调用方处理。</para>
    /// </remarks>
    public static void Initialize(string baseDir, ExecutionProviderMode ep)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        // OpenVINO 模式用 ep-openvino 子目录；CPU 与 DirectML 都用 ep-dml 子目录
        // （DirectML 包的 onnxruntime.dll 也包含完整 CPU EP，未调用 AppendExecutionProvider_DML 时即纯 CPU 推理）
        var subDir = ep == ExecutionProviderMode.OpenVINO ? EpOpenVinoSubDir : EpDmlSubDir;
        var srcDir = Path.Combine(baseDir, subDir);

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
