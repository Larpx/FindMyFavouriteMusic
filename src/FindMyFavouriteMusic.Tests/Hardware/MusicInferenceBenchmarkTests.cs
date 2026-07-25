using System.Diagnostics;
using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Hardware;

/// <summary>
/// 音乐推理性能对比测试：使用真实 flac 音乐文件，
/// 对 VGGish 与 MERT 两个模型分别测量不同 Execution Provider 下的加载耗时和推理耗时。
/// </summary>
/// <remarks>
/// <para>v2.0 起仅测试 OpenVINO + CPU 双 EP 架构（DirectML 已移除）。</para>
/// <para><b>测试目标：</b>回答"OpenVINO（GPU/NPU/AUTO）vs CPU，哪种最快"。</para>
/// <para><b>测试输入：</b>仓库根目录 <c>Models/ナナツカゼ,PIKASONIC,なこたんまる - 再生.flac</c>。</para>
/// <para><b>EP 选择机制：</b>由 <c>EpNativeLoaderInitializer</c>（ModuleInitializer）读取环境变量
/// <c>FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider</c> 决定测试启动时复制哪种 EP 的 native 库到根目录。
/// 由于 native 库加载后无法卸载，单次 <c>dotnet test</c> 运行只能测试一种 EP。</para>
/// <para><b>对比方式：</b></para>
/// <para>- <see cref="Benchmark_MusicInference_AcceleratorVsCpu"/>：当前 EP（OpenVINO）vs CPU，单次运行得到对比表；</para>
/// <para>- <see cref="Benchmark_MusicInference_CurrentEp_Only"/>：仅运行当前 EP，输出耗时，便于用户运行多次对比多种 EP。</para>
/// <para><b>使用示例（PowerShell）：</b></para>
/// <para>1. <c>$env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider = "CPU"; dotnet test --filter Benchmark_MusicInference_CurrentEp_Only</c></para>
/// <para>2. <c>$env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider = "OpenVINO"; $env:FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice = "GPU"; dotnet test --filter Benchmark_MusicInference_CurrentEp_Only</c></para>
/// <para>3. <c>$env:FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider = "OpenVINO"; $env:FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice = "NPU"; dotnet test --filter Benchmark_MusicInference_CurrentEp_Only</c></para>
/// <para><b>OpenVINO 设备选择：</b>OpenVINO EP 支持 NPU/GPU/AUTO 三种目标设备，
/// 通过 <c>FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice</c> 环境变量切换（默认 GPU）。</para>
/// <para><b>已知行为：</b>OpenVINO EP 对动态形状支持较好，MERT 在 OpenVINO(GPU) 下能直接推理成功。
/// 但 NPU 设备对部分算子可能不兼容，触发 CPU 回退时耗时包含"OpenVINO 失败 + 重建 CPU 会话 + CPU 推理"。</para>
/// </remarks>
public class MusicInferenceBenchmarkTests
{
    private const string AudioFileName = "ナナツカゼ,PIKASONIC,なこたんまる - 再生.flac";
    private const int TargetSampleRate = 16000;

    private readonly ITestOutputHelper _output;

    public MusicInferenceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 对指定 flac 音乐文件，测量 VGGish 与 MERT 在"当前配置的加速 EP（OpenVINO）"与"CPU"模式下的加载与推理耗时。
    /// </summary>
    /// <remarks>
    /// <para>当前 EP 由环境变量 <c>FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider</c> 决定
    /// （由 ModuleInitializer 在测试启动时复制对应 native 库）。</para>
    /// <para>未设置环境变量时默认 OpenVINO（与生产默认值一致）。</para>
    /// <para>测试输出对比表，标注当前 EP 与是否触发 CPU 回退。</para>
    /// </remarks>
    [Fact]
    public async Task Benchmark_MusicInference_AcceleratorVsCpu()
    {
        // 1. 定位音频文件
        var audioPath = TryResolvePath("Models", AudioFileName);
        if (SkipIfMissing(audioPath, AudioFileName)) return;

        // 2. 解码音频（只解码一次，所有模型共用同一份 PCM 样本）
        var featureOptions = Options.Create(new FeatureExtractionOptions());
        var decoder = new AudioDecoder(featureOptions, NullLogger<AudioDecoder>.Instance);

        var infoResult = await decoder.GetAudioInfoAsync(audioPath!);
        if (!infoResult.IsSuccess)
        {
            _output.WriteLine($"无法读取音频信息: {infoResult.Error}");
            return;
        }

        var decodeSw = Stopwatch.StartNew();
        var decodeResult = await decoder.DecodeAsync(audioPath!);
        decodeSw.Stop();

        if (!decodeResult.IsSuccess || decodeResult.Value is null)
        {
            _output.WriteLine($"音频解码失败: {decodeResult.Error}");
            return;
        }

        var samples = decodeResult.Value;
        var info = infoResult.Value!;

        // 3. 读取当前生效的 EP 配置（与 EpNativeLoaderInitializer 保持一致）
        var (acceleratorEp, openVinoDevice) = ResolveCurrentEpConfig();
        var acceleratorEpLabel = FormatEpLabel(acceleratorEp, openVinoDevice);

        _output.WriteLine("===== 音频文件信息 =====");
        _output.WriteLine($"  文件: {AudioFileName}");
        _output.WriteLine($"  时长: {info.Duration.TotalSeconds:F1} 秒");
        _output.WriteLine($"  原始采样率: {info.SampleRate} Hz");
        _output.WriteLine($"  声道: {info.Channels}");
        _output.WriteLine($"  解码耗时: {decodeSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  PCM 样本数（重采样至 {TargetSampleRate}Hz 单声道后）: {samples.Length}");
        _output.WriteLine($"  当前待测加速 EP: {acceleratorEpLabel}");
        _output.WriteLine(string.Empty);

        // 4. VGGish 对比：当前加速 EP vs CPU
        var vggishLoadAcc = 0L; var vggishInferAcc = 0L; var vggishEpAcc = "";
        var vggishLoadCpu = 0L; var vggishInferCpu = 0L; var vggishEpCpu = "CPU";

        var vggishPath = TryResolvePath("Models", "VGGish.onnx");
        if (vggishPath != null)
        {
            (vggishLoadAcc, vggishInferAcc, vggishEpAcc) = await RunOnce<DeepFeatureExtractor>(
                "VGGish", vggishPath, samples, acceleratorEp, openVinoDevice,
                (opt, acc, log) => new DeepFeatureExtractor(opt, acc, log), DeepModelType.VGGish);
            (vggishLoadCpu, vggishInferCpu, vggishEpCpu) = await RunOnce<DeepFeatureExtractor>(
                "VGGish", vggishPath, samples, ExecutionProviderMode.CPU, openVinoDevice,
                (opt, acc, log) => new DeepFeatureExtractor(opt, acc, log), DeepModelType.VGGish);
        }
        else
        {
            _output.WriteLine("跳过 VGGish：未找到 Models/VGGish.onnx");
        }

        // 5. MERT 对比
        var mertLoadAcc = 0L; var mertInferAcc = 0L; var mertEpAcc = "";
        var mertLoadCpu = 0L; var mertInferCpu = 0L; var mertEpCpu = "CPU";

        var mertPath = TryResolvePath("Models", "MERT-v1-95M.onnx");
        if (mertPath != null)
        {
            (mertLoadAcc, mertInferAcc, mertEpAcc) = await RunOnce<MertFeatureExtractor>(
                "MERT", mertPath, samples, acceleratorEp, openVinoDevice,
                (opt, acc, log) => new MertFeatureExtractor(opt, acc, log), DeepModelType.MERT);
            (mertLoadCpu, mertInferCpu, mertEpCpu) = await RunOnce<MertFeatureExtractor>(
                "MERT", mertPath, samples, ExecutionProviderMode.CPU, openVinoDevice,
                (opt, acc, log) => new MertFeatureExtractor(opt, acc, log), DeepModelType.MERT);
        }
        else
        {
            _output.WriteLine("跳过 MERT：未找到 Models/MERT-v1-95M.onnx");
        }

        // 6. 汇总对比表
        _output.WriteLine(string.Empty);
        _output.WriteLine("===== 性能对比汇总 =====");
        _output.WriteLine($"{"模型",-8} {"模式",-20} {"加载(ms)",-10} {"推理(ms)",-12} {"最终EP",-16}");
        _output.WriteLine(new string('-', 70));

        if (vggishPath != null)
        {
            _output.WriteLine($"{"VGGish",-8} {acceleratorEpLabel,-20} {vggishLoadAcc,-10} {vggishInferAcc,-12} {vggishEpAcc,-16}");
            _output.WriteLine($"{"VGGish",-8} {"CPU",-20} {vggishLoadCpu,-10} {vggishInferCpu,-12} {vggishEpCpu,-16}");
            PrintSpeedupConclusion("VGGish", acceleratorEpLabel, vggishEpAcc, vggishInferAcc, vggishInferCpu);
        }

        if (mertPath != null)
        {
            _output.WriteLine($"{"MERT",-8} {acceleratorEpLabel,-20} {mertLoadAcc,-10} {mertInferAcc,-12} {mertEpAcc,-16}");
            _output.WriteLine($"{"MERT",-8} {"CPU",-20} {mertLoadCpu,-10} {mertInferCpu,-12} {mertEpCpu,-16}");
            PrintSpeedupConclusion("MERT", acceleratorEpLabel, mertEpAcc, mertInferAcc, mertInferCpu);
        }

        // 简单断言保证测试有意义
        samples.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 仅运行当前生效的 EP（不与 CPU 对比），输出 VGGish 与 MERT 的加载与推理耗时。
    /// </summary>
    /// <remarks>
    /// <para><b>使用场景：</b>用户依次运行多次（设置不同 <c>FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider</c>
    /// 与 <c>FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice</c>），
    /// 每次输出当前 EP 的耗时，便于横向对比 CPU / OpenVINO(GPU/NPU/AUTO) 等多种 EP。</para>
    /// <para><b>与 <see cref="Benchmark_MusicInference_AcceleratorVsCpu"/> 的差异：</b>
    /// 后者在单次运行内对比"当前 EP vs CPU"，本测试只测当前 EP，节省时间，适合多次运行对比。</para>
    /// </remarks>
    [Fact]
    public async Task Benchmark_MusicInference_CurrentEp_Only()
    {
        // 1. 定位音频文件
        var audioPath = TryResolvePath("Models", AudioFileName);
        if (SkipIfMissing(audioPath, AudioFileName)) return;

        // 2. 解码音频
        var featureOptions = Options.Create(new FeatureExtractionOptions());
        var decoder = new AudioDecoder(featureOptions, NullLogger<AudioDecoder>.Instance);

        var decodeResult = await decoder.DecodeAsync(audioPath!);
        if (!decodeResult.IsSuccess || decodeResult.Value is null)
        {
            _output.WriteLine($"音频解码失败: {decodeResult.Error}");
            return;
        }

        var samples = decodeResult.Value;

        // 3. 读取当前 EP 配置
        var (acceleratorEp, openVinoDevice) = ResolveCurrentEpConfig();
        var epLabel = FormatEpLabel(acceleratorEp, openVinoDevice);

        _output.WriteLine("===== 当前 EP 单独测试 =====");
        _output.WriteLine($"  当前 EP: {epLabel}");
        _output.WriteLine($"  音频样本数: {samples.Length} (采样率 {TargetSampleRate}Hz)");
        _output.WriteLine(string.Empty);

        // 4. VGGish
        var vggishPath = TryResolvePath("Models", "VGGish.onnx");
        if (vggishPath != null)
        {
            var (loadMs, inferMs, actualEp) = await RunOnce<DeepFeatureExtractor>(
                "VGGish", vggishPath, samples, acceleratorEp, openVinoDevice,
                (opt, acc, log) => new DeepFeatureExtractor(opt, acc, log), DeepModelType.VGGish);
            _output.WriteLine($"  VGGish: 加载={loadMs}ms, 推理={inferMs}ms, 实际EP={actualEp}");
        }
        else
        {
            _output.WriteLine("  跳过 VGGish：未找到 Models/VGGish.onnx");
        }

        // 5. MERT
        var mertPath = TryResolvePath("Models", "MERT-v1-95M.onnx");
        if (mertPath != null)
        {
            var (loadMs, inferMs, actualEp) = await RunOnce<MertFeatureExtractor>(
                "MERT", mertPath, samples, acceleratorEp, openVinoDevice,
                (opt, acc, log) => new MertFeatureExtractor(opt, acc, log), DeepModelType.MERT);
            _output.WriteLine($"  MERT: 加载={loadMs}ms, 推理={inferMs}ms, 实际EP={actualEp}");
        }
        else
        {
            _output.WriteLine("  跳过 MERT：未找到 Models/MERT-v1-95M.onnx");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine($"提示：分别设置 FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider=CPU/OpenVINO 与 FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice=GPU/NPU/AUTO 运行多次以对比多种 EP");

        samples.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 在指定 EP 模式下运行一次"加载 + 推理"，返回 (加载耗时ms, 推理耗时ms, 最终EP)。
    /// </summary>
    /// <typeparam name="T">提取器具体类型，仅用于日志泛型参数</typeparam>
    /// <param name="modelLabel">模型标签，用于输出</param>
    /// <param name="modelPath">模型文件路径</param>
    /// <param name="samples">PCM 采样数据</param>
    /// <param name="ep">本次运行的 EP 模式（CPU / OpenVINO）</param>
    /// <param name="openVinoDevice">OpenVINO 目标设备（仅 EP=OpenVINO 时生效）</param>
    /// <param name="factory">提取器工厂方法</param>
    /// <param name="modelType">深度模型类型（VGGish / MERT）</param>
    /// <returns>(加载耗时ms, 推理耗时ms, 最终实际生效的 EP 名称)</returns>
    private async Task<(long loadMs, long inferMs, string ep)> RunOnce<T>(
        string modelLabel,
        string modelPath,
        float[] samples,
        ExecutionProviderMode ep,
        OpenVinoDeviceType openVinoDevice,
        Func<IOptions<OnnxModelOptions>, HardwareAccelerator, ILogger<T>, IDeepFeatureExtractor> factory,
        DeepModelType modelType) where T : class
    {
        var modeLabel = FormatEpLabel(ep, openVinoDevice);
        _output.WriteLine($"--- {modelLabel} [{modeLabel}] ---");

        // OnnxModelOptions 的决策逻辑：ExecutionProvider=CPU 强制 CPU，否则按 ExecutionProvider 字段选择 OpenVINO 设备
        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ep,
            OpenVinoDevice = openVinoDevice
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var extractor = factory(options, accelerator, NullLogger<T>.Instance);

        _output.WriteLine($"  NPU 检测: Available={accelerator.IsNpuAvailable}, " +
                          $"Device={accelerator.NpuDeviceName ?? "(未检测到)"}");

        // 加载计时
        var loadSw = Stopwatch.StartNew();
        var loadResult = extractor.LoadModel(modelPath, modelType);
        loadSw.Stop();

        _output.WriteLine($"  加载后 EP: {accelerator.ActiveExecutionProvider}");
        _output.WriteLine($"  加载耗时: {loadSw.ElapsedMilliseconds} ms");

        if (!loadResult.IsSuccess)
        {
            _output.WriteLine($"  加载失败: {loadResult.Error}");
            return (loadSw.ElapsedMilliseconds, 0, accelerator.ActiveExecutionProvider);
        }

        // 推理计时
        var epBefore = accelerator.ActiveExecutionProvider;
        var inferSw = Stopwatch.StartNew();
        var result = await extractor.ExtractAsync(samples, TargetSampleRate);
        inferSw.Stop();
        var epAfter = accelerator.ActiveExecutionProvider;

        _output.WriteLine($"  推理前 EP: {epBefore}");
        _output.WriteLine($"  推理后 EP: {epAfter}");
        _output.WriteLine($"  推理耗时: {inferSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  IsSuccess: {result.IsSuccess}");

        if (result.IsSuccess && result.Value is not null)
        {
            _output.WriteLine($"  输出维度: {result.Value.Length}");
            _output.WriteLine($"  输出向量前 5 维: [{string.Join(", ", result.Value.Take(5).Select(v => v.ToString("F4")))}]");
        }
        else
        {
            _output.WriteLine($"  错误: {result.Error}");
        }

        if (epBefore != epAfter)
        {
            _output.WriteLine($"  >> EP 切换: {epBefore} -> {epAfter}（触发推理失败回退）");
        }

        _output.WriteLine(string.Empty);
        return (loadSw.ElapsedMilliseconds, inferSw.ElapsedMilliseconds, epAfter);
    }

    /// <summary>
    /// 读取环境变量解析当前测试的 EP 配置，与 <c>EpNativeLoaderInitializer</c> 决策逻辑保持一致。
    /// </summary>
    /// <returns>(EP 模式, OpenVINO 目标设备)</returns>
    /// <remarks>
    /// 未设置环境变量时返回生产默认值（OpenVINO + GPU）。
    /// </remarks>
    private static (ExecutionProviderMode ep, OpenVinoDeviceType device) ResolveCurrentEpConfig()
    {
        var epRaw = Environment.GetEnvironmentVariable("FINDMYFAVOURITEMUSIC_OnnxModel__ExecutionProvider");
        var ep = epRaw?.Trim().ToLowerInvariant() switch
        {
            "cpu" => ExecutionProviderMode.CPU,
            "openvino" or "ov" => ExecutionProviderMode.OpenVINO,
            _ => ExecutionProviderMode.OpenVINO // 生产默认值
        };

        var deviceRaw = Environment.GetEnvironmentVariable("FINDMYFAVOURITEMUSIC_OnnxModel__OpenVinoDevice");
        var device = deviceRaw?.Trim().ToLowerInvariant() switch
        {
            "npu" => OpenVinoDeviceType.NPU,
            "gpu" => OpenVinoDeviceType.GPU,
            "auto" => OpenVinoDeviceType.AUTO,
            _ => OpenVinoDeviceType.GPU // 生产默认值
        };

        return (ep, device);
    }

    /// <summary>
    /// 格式化 EP 标签用于输出，OpenVINO 模式附带目标设备。
    /// </summary>
    /// <param name="ep">EP 模式</param>
    /// <param name="device">OpenVINO 目标设备（仅 EP=OpenVINO 时使用）</param>
    /// <returns>形如 "OpenVINO(GPU)"、"OpenVINO(NPU)"、"CPU" 的标签字符串</returns>
    private static string FormatEpLabel(ExecutionProviderMode ep, OpenVinoDeviceType device)
    {
        return ep switch
        {
            ExecutionProviderMode.OpenVINO => $"OpenVINO({device})",
            _ => "CPU"
        };
    }

    /// <summary>
    /// 输出加速比结论，区分"加速成功"、"触发 CPU 回退"、"加载失败"三种情况。
    /// </summary>
    /// <param name="modelName">模型名（VGGish / MERT）</param>
    /// <param name="expectedEpLabel">期望的加速 EP 标签</param>
    /// <param name="actualEp">实际生效的 EP（推理后）</param>
    /// <param name="inferMs">加速模式推理耗时</param>
    /// <param name="cpuInferMs">CPU 模式推理耗时</param>
    private void PrintSpeedupConclusion(
        string modelName, string expectedEpLabel, string actualEp, long inferMs, long cpuInferMs)
    {
        if (inferMs <= 0)
        {
            _output.WriteLine($"  -> {modelName}: 加速 EP 模式加载/推理失败，无加速比数据");
            return;
        }

        if (actualEp == "CPU")
        {
            _output.WriteLine($"  -> {modelName}: 加速 EP 触发 CPU 回退（推理最终走 CPU），加速模式 {inferMs}ms vs 直接 CPU {cpuInferMs}ms，" +
                              $"{(inferMs > cpuInferMs ? $"慢 {inferMs - cpuInferMs}ms（含回退重建开销）" : "略快（属测量波动）")}");
        }
        else if (cpuInferMs > 0)
        {
            var speedup = (double)cpuInferMs / inferMs;
            _output.WriteLine($"  -> {modelName}: {expectedEpLabel} 加速比 {speedup:F2}x （{expectedEpLabel} {inferMs}ms vs CPU {cpuInferMs}ms）");
        }
    }

    /// <summary>
    /// 从测试 bin 目录向上查找仓库根目录，拼接子目录与文件名得到绝对路径。
    /// </summary>
    private static string? TryResolvePath(string subDir, string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.Name.Equals("find-my-favourite-music", StringComparison.OrdinalIgnoreCase))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            return null;
        }

        var path = Path.Combine(dir.FullName, subDir, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 文件不存在时跳过测试并返回 true。
    /// </summary>
    private bool SkipIfMissing(string? path, string description)
    {
        if (string.IsNullOrEmpty(path))
        {
            _output.WriteLine($"跳过测试：未找到 {description}");
            return true;
        }
        return false;
    }
}
