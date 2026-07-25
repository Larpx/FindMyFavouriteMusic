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
/// 对 VGGish 与 MERT 两个模型分别测量 NPU(DirectML) 与 CPU 模式下的加载耗时和推理耗时。
/// </summary>
/// <remarks>
/// <para><b>测试目标：</b>回答"使用 NPU 时，耗时是否缩短"。</para>
/// <para><b>测试输入：</b>仓库根目录 <c>Models/ナナツカゼ,PIKASONIC,なこたんまる - 再生.flac</c>。</para>
/// <para><b>测试方法：</b></para>
/// <para>1. 一次性解码 flac 得到 PCM 样本（不计入模型推理对比）；</para>
/// <para>2. 对每个模型 × 每个 EP 模式（PreferNpu=true / false），分别测量"加载耗时"与"推理耗时"；</para>
/// <para>3. 输出对比表，标注最终生效 EP 与是否触发 CPU 回退。</para>
/// <para><b>已知行为：</b>MERT 含动态形状 Reshape 算子，DirectML EP 推理会失败并触发 CPU 回退，
/// 因此 MERT 在 NPU 模式下的耗时包含"DirectML 失败 + 重建 CPU 会话 + CPU 推理"，预期比直接 CPU 模式慢。</para>
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
    /// 对指定 flac 音乐文件，测量 VGGish 与 MERT 在 NPU/CPU 模式下的加载与推理耗时。
    /// </summary>
    [Fact]
    public async Task Benchmark_MusicInference_NpuVsCpu()
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

        _output.WriteLine("===== 音频文件信息 =====");
        _output.WriteLine($"  文件: {AudioFileName}");
        _output.WriteLine($"  时长: {info.Duration.TotalSeconds:F1} 秒");
        _output.WriteLine($"  原始采样率: {info.SampleRate} Hz");
        _output.WriteLine($"  声道: {info.Channels}");
        _output.WriteLine($"  解码耗时: {decodeSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  PCM 样本数（重采样至 {TargetSampleRate}Hz 单声道后）: {samples.Length}");
        _output.WriteLine(string.Empty);

        // 3. VGGish 对比
        var vggishLoadNpu = 0L; var vggishInferNpu = 0L; var vggishEpNpu = "";
        var vggishLoadCpu = 0L; var vggishInferCpu = 0L; var vggishEpCpu = "CPU";

        var vggishPath = TryResolvePath("Models", "VGGish.onnx");
        if (vggishPath != null)
        {
            (vggishLoadNpu, vggishInferNpu, vggishEpNpu) = await RunOnce<DeepFeatureExtractor>(
                "VGGish", vggishPath, samples, preferNpu: true,
                (opt, acc, log) => new DeepFeatureExtractor(opt, acc, log), DeepModelType.VGGish);
            (vggishLoadCpu, vggishInferCpu, vggishEpCpu) = await RunOnce<DeepFeatureExtractor>(
                "VGGish", vggishPath, samples, preferNpu: false,
                (opt, acc, log) => new DeepFeatureExtractor(opt, acc, log), DeepModelType.VGGish);
        }
        else
        {
            _output.WriteLine("跳过 VGGish：未找到 Models/VGGish.onnx");
        }

        // 4. MERT 对比
        var mertLoadNpu = 0L; var mertInferNpu = 0L; var mertEpNpu = "";
        var mertLoadCpu = 0L; var mertInferCpu = 0L; var mertEpCpu = "CPU";

        var mertPath = TryResolvePath("Models", "MERT-v1-95M.onnx");
        if (mertPath != null)
        {
            (mertLoadNpu, mertInferNpu, mertEpNpu) = await RunOnce<MertFeatureExtractor>(
                "MERT", mertPath, samples, preferNpu: true,
                (opt, acc, log) => new MertFeatureExtractor(opt, acc, log), DeepModelType.MERT);
            (mertLoadCpu, mertInferCpu, mertEpCpu) = await RunOnce<MertFeatureExtractor>(
                "MERT", mertPath, samples, preferNpu: false,
                (opt, acc, log) => new MertFeatureExtractor(opt, acc, log), DeepModelType.MERT);
        }
        else
        {
            _output.WriteLine("跳过 MERT：未找到 Models/MERT-v1-95M.onnx");
        }

        // 5. 汇总对比表
        _output.WriteLine(string.Empty);
        _output.WriteLine("===== 性能对比汇总 =====");
        _output.WriteLine($"{"模型",-8} {"模式",-18} {"加载(ms)",-10} {"推理(ms)",-12} {"最终EP",-10}");
        _output.WriteLine(new string('-', 60));

        if (vggishPath != null)
        {
            _output.WriteLine($"{"VGGish",-8} {"NPU(DirectML)",-18} {vggishLoadNpu,-10} {vggishInferNpu,-12} {vggishEpNpu,-10}");
            _output.WriteLine($"{"VGGish",-8} {"CPU",-18} {vggishLoadCpu,-10} {vggishInferCpu,-12} {vggishEpCpu,-10}");
            if (vggishInferCpu > 0 && vggishInferNpu > 0 && vggishEpNpu == "DirectML")
            {
                var speedup = (double)vggishInferCpu / vggishInferNpu;
                _output.WriteLine($"  -> VGGish NPU 加速比: {speedup:F2}x （{vggishInferCpu}ms / {vggishInferNpu}ms）");
            }
            else if (vggishEpNpu == "CPU")
            {
                _output.WriteLine($"  -> VGGish NPU 模式实际回退到 CPU（DirectML 加载失败或不可用）");
            }
        }

        if (mertPath != null)
        {
            _output.WriteLine($"{"MERT",-8} {"NPU(DirectML)",-18} {mertLoadNpu,-10} {mertInferNpu,-12} {mertEpNpu,-10}");
            _output.WriteLine($"{"MERT",-8} {"CPU",-18} {mertLoadCpu,-10} {mertInferCpu,-12} {mertEpCpu,-10}");
            if (mertEpNpu == "CPU" && mertInferNpu > mertInferCpu && mertInferCpu > 0)
            {
                var overhead = mertInferNpu - mertInferCpu;
                _output.WriteLine($"  -> MERT NPU 模式触发 CPU 回退，额外开销: +{overhead}ms （含 DirectML 失败 + 重建 CPU 会话）");
            }
            else if (mertEpNpu == "DirectML" && mertInferCpu > 0 && mertInferNpu > 0)
            {
                var speedup = (double)mertInferCpu / mertInferNpu;
                _output.WriteLine($"  -> MERT NPU 加速比: {speedup:F2}x （{mertInferCpu}ms / {mertInferNpu}ms）");
            }
        }

        // 6. 结论
        _output.WriteLine(string.Empty);
        _output.WriteLine("===== 结论 =====");
        if (vggishPath != null)
        {
            if (vggishEpNpu == "DirectML")
            {
                var faster = vggishInferNpu < vggishInferCpu;
                _output.WriteLine($"VGGish: NPU 模式推理 {vggishInferNpu}ms vs CPU {vggishInferCpu}ms，" +
                                  $"{(faster ? "NPU 更快" : "NPU 未缩短（可能 DirectML 首次编译开销或算子仍走 CPU）")}");
            }
            else
            {
                _output.WriteLine($"VGGish: NPU 模式未生效（EP={vggishEpNpu}），无法对比加速效果");
            }
        }
        if (mertPath != null)
        {
            if (mertEpNpu == "CPU")
            {
                var faster = mertInferNpu < mertInferCpu;
                _output.WriteLine($"MERT: DirectML EP 不兼容（Reshape 动态形状算子失败），已自动回退 CPU；" +
                                  $"NPU 模式 {mertInferNpu}ms vs 直接 CPU {mertInferCpu}ms，" +
                                  $"{(faster ? "NPU 模式略快（属测量波动，两者最终均跑在 CPU 上）" : "NPU 模式更慢（含回退重建开销）")}");
            }
            else if (mertEpNpu == "DirectML")
            {
                var faster = mertInferNpu < mertInferCpu;
                _output.WriteLine($"MERT: NPU 模式推理 {mertInferNpu}ms vs CPU {mertInferCpu}ms，" +
                                  $"{(faster ? "NPU 更快" : "NPU 未缩短")}");
            }
        }

        // 简单断言保证测试有意义
        samples.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 在指定 EP 模式下运行一次"加载 + 推理"，返回 (加载耗时ms, 推理耗时ms, 最终EP)。
    /// </summary>
    private async Task<(long loadMs, long inferMs, string ep)> RunOnce<T>(
        string modelLabel,
        string modelPath,
        float[] samples,
        bool preferNpu,
        Func<IOptions<OnnxModelOptions>, HardwareAccelerator, ILogger<T>, IDeepFeatureExtractor> factory,
        DeepModelType modelType) where T : class
    {
        var modeLabel = preferNpu ? "NPU(DirectML)" : "CPU";
        _output.WriteLine($"--- {modelLabel} [{modeLabel}] ---");

        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            PreferNpu = preferNpu
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
