using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Hardware;

/// <summary>
/// OpenVINO 加速集成测试：使用仓库 Models/ 目录下的真实 ONNX 模型，
/// 验证 <see cref="HardwareAccelerator"/> 与深度特征提取器能否实际调用 NPU/GPU 进行推理。
/// </summary>
/// <remarks>
/// <para>v2.0 起仅测试 OpenVINO + CPU 双 EP 架构（DirectML 已移除）。</para>
/// <para><b>测试依赖：</b>仓库根目录下的 <c>Models/VGGish.onnx</c> 与 <c>Models/MERT-v1-95M.onnx</c>。</para>
/// <para><b>测试目标：</b></para>
/// <para>1. 验证模型加载时 EP 选择逻辑能正确执行（CPU 或 OpenVINO）；</para>
/// <para>2. 验证实际推理能完成且输出维度正确；</para>
/// <para>3. 通过 <see cref="ITestOutputHelper"/> 输出当前生效 EP，供人工确认 NPU/GPU 是否被调用。</para>
/// <para><b>关于"是否调用 NPU/GPU"的判定：</b>测试仅能确认 EP=OpenVINO(NPU/GPU/AUTO)，
/// 无法直接观测硬件利用率；若需进一步验证，建议配合任务管理器或 Intel NPU 工具查看推理期间硬件负载。</para>
/// </remarks>
public class NpuModelIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public NpuModelIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 加载 VGGish 模型，验证加载成功并输出实际生效的 EP。
    /// </summary>
    [Fact]
    public void LoadModel_VGGish_ReturnsSuccess_AndReportsEp()
    {
        // Arrange
        var modelPath = TryResolveModelPath("VGGish.onnx");
        if (SkipIfModelMissing(modelPath, "VGGish.onnx")) return;

        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false, // 避免构造时自动加载，手动控制
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var extractor = new DeepFeatureExtractor(
            options, accelerator, NullLogger<DeepFeatureExtractor>.Instance);

        // Act
        var result = extractor.LoadModel(modelPath!, DeepModelType.VGGish);

        // Assert
        result.IsSuccess.Should().BeTrue($"VGGish 模型应能成功加载: {modelPath}");
        extractor.IsModelLoaded.Should().BeTrue();
        extractor.FeatureDimension.Should().Be(128);

        _output.WriteLine($"VGGish 加载完成");
        _output.WriteLine($"  模型路径: {modelPath}");
        _output.WriteLine($"  NPU 检测: Available={accelerator.IsNpuAvailable}, Device={accelerator.NpuDeviceName ?? "(未检测到)"}");
        _output.WriteLine($"  实际 EP: {accelerator.ActiveExecutionProvider}");
        _output.WriteLine($"  特征维度: {extractor.FeatureDimension}");
    }

    /// <summary>
    /// 加载 MERT 模型，验证加载成功并输出实际生效的 EP。
    /// </summary>
    [Fact]
    public void LoadModel_MERT_ReturnsSuccess_AndReportsEp()
    {
        // Arrange
        var modelPath = TryResolveModelPath("MERT-v1-95M.onnx");
        if (SkipIfModelMissing(modelPath, "MERT-v1-95M.onnx")) return;

        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var extractor = new MertFeatureExtractor(
            options, accelerator, NullLogger<MertFeatureExtractor>.Instance);

        // Act
        var result = extractor.LoadModel(modelPath!, DeepModelType.MERT);

        // Assert
        result.IsSuccess.Should().BeTrue($"MERT 模型应能成功加载: {modelPath}");
        extractor.IsModelLoaded.Should().BeTrue();
        extractor.FeatureDimension.Should().Be(768);

        _output.WriteLine($"MERT 加载完成");
        _output.WriteLine($"  模型路径: {modelPath}");
        _output.WriteLine($"  NPU 检测: Available={accelerator.IsNpuAvailable}, Device={accelerator.NpuDeviceName ?? "(未检测到)"}");
        _output.WriteLine($"  实际 EP: {accelerator.ActiveExecutionProvider}");
        _output.WriteLine($"  特征维度: {extractor.FeatureDimension}");
    }

    /// <summary>
    /// 使用 VGGish 对一段合成正弦波音频执行实际推理，
    /// 验证返回向量维度为 128，并输出 EP 信息以便人工核查 NPU/GPU 调用情况。
    /// </summary>
    [Fact]
    public async Task ExtractAsync_VGGish_Returns128Dimension()
    {
        // Arrange
        var modelPath = TryResolveModelPath("VGGish.onnx");
        if (SkipIfModelMissing(modelPath, "VGGish.onnx")) return;

        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var extractor = new DeepFeatureExtractor(
            options, accelerator, NullLogger<DeepFeatureExtractor>.Instance);

        extractor.LoadModel(modelPath!, DeepModelType.VGGish);

        // 生成 1 秒 16kHz 正弦波（VGGish 一帧需 0.96s × 16000 = 15360 样本，1 秒数据足够一帧）
        const int sampleRate = 16000;
        const double durationSeconds = 1.0;
        var samples = GenerateSineWave(frequency: 440.0, sampleRate, durationSeconds);

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await extractor.ExtractAsync(samples, sampleRate);
        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue("VGGish 推理应成功");
        result.Value!.Length.Should().Be(128, "VGGish 输出固定为 128 维");

        _output.WriteLine($"VGGish 推理完成");
        _output.WriteLine($"  EP: {accelerator.ActiveExecutionProvider}");
        _output.WriteLine($"  输入样本数: {samples.Length} (采样率 {sampleRate}Hz, 时长 {durationSeconds}s)");
        _output.WriteLine($"  输出维度: {result.Value.Length}");
        _output.WriteLine($"  推理耗时: {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  输出向量前 5 维: [{string.Join(", ", result.Value.Take(5).Select(v => v.ToString("F4")))}]");
    }

    /// <summary>
    /// 验证 MERT 在 OpenVINO EP 推理失败时能自动回退 CPU EP 并成功完成推理。
    /// </summary>
    /// <remarks>
    /// <para><b>测试场景：</b>MERT 模型含动态形状 Reshape 算子，某些 OpenVINO 设备（如 NPU）可能无法执行。</para>
    /// <para><b>预期行为：</b>提取器捕获推理异常后，自动用 CPU EP 重建会话并重试推理，
    /// 最终返回 768 维特征向量，<see cref="IHardwareAccelerator.ActiveExecutionProvider"/> 从 OpenVINO 切换为 CPU。</para>
    /// <para>此测试验证回退机制保证了 MERT 模式在 OpenVINO 不兼容时仍可用。</para>
    /// </remarks>
    [Fact]
    public async Task ExtractAsync_MERT_FallsBackToCpu_WhenOpenVinoFails()
    {
        // Arrange
        var modelPath = TryResolveModelPath("MERT-v1-95M.onnx");
        if (SkipIfModelMissing(modelPath, "MERT-v1-95M.onnx")) return;

        var options = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var extractor = new MertFeatureExtractor(
            options, accelerator, NullLogger<MertFeatureExtractor>.Instance);

        extractor.LoadModel(modelPath!, DeepModelType.MERT);
        var epBeforeInference = accelerator.ActiveExecutionProvider;

        // 生成 5 秒 16kHz 正弦波（MERT 内部会重采样到 24kHz，5 秒为一帧）
        const int sampleRate = 16000;
        const double durationSeconds = 5.0;
        var samples = GenerateSineWave(frequency: 440.0, sampleRate, durationSeconds);

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await extractor.ExtractAsync(samples, sampleRate);
        sw.Stop();
        var epAfterInference = accelerator.ActiveExecutionProvider;

        // Assert & 诊断输出
        _output.WriteLine($"MERT 推理结果（回退机制验证）");
        _output.WriteLine($"  推理前 EP: {epBeforeInference}");
        _output.WriteLine($"  推理后 EP: {epAfterInference}");
        _output.WriteLine($"  输入样本数: {samples.Length} (采样率 {sampleRate}Hz, 时长 {durationSeconds}s)");
        _output.WriteLine($"  推理耗时: {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"  IsSuccess: {result.IsSuccess}");

        if (epBeforeInference != "CPU" && epBeforeInference != epAfterInference)
        {
            // OpenVINO 启用但推理失败场景：MERT 推理应触发 CPU 回退，最终成功
            result.IsSuccess.Should().BeTrue("MERT 在 OpenVINO 失败后应回退 CPU 并成功推理");
            result.Value!.Length.Should().Be(768, "MERT 输出固定为 768 维");
            epAfterInference.Should().Be("CPU", "OpenVINO 推理失败后应回退到 CPU EP");

            _output.WriteLine($"  输出维度: {result.Value.Length}");
            _output.WriteLine($"  输出向量前 5 维: [{string.Join(", ", result.Value.Take(5).Select(v => v.ToString("F4")))}]");
            _output.WriteLine("  结论: MERT + OpenVINO 失败后成功回退 CPU EP，推理完成");
        }
        else if (epBeforeInference == "CPU")
        {
            // OpenVINO 不可用场景：直接 CPU 推理
            result.IsSuccess.Should().BeTrue("CPU EP 下 MERT 推理应成功");
            result.Value!.Length.Should().Be(768, "MERT 输出固定为 768 维");
            epAfterInference.Should().Be("CPU");

            _output.WriteLine($"  输出维度: {result.Value.Length}");
            _output.WriteLine("  结论: MERT 直接使用 CPU EP 推理成功（OpenVINO 不可用）");
        }
        else
        {
            // OpenVINO 启用且推理成功场景：未触发回退
            result.IsSuccess.Should().BeTrue("MERT 在 OpenVINO 下应成功推理");
            result.Value!.Length.Should().Be(768, "MERT 输出固定为 768 维");
            epAfterInference.Should().Be(epBeforeInference, "OpenVINO 推理成功时不应切换 EP");

            _output.WriteLine($"  输出维度: {result.Value.Length}");
            _output.WriteLine($"  结论: MERT 直接使用 {epBeforeInference} EP 推理成功（未触发回退）");
        }
    }

    /// <summary>
    /// 比较开启 OpenVINO 与关闭（强制 CPU）时的推理耗时，作为 NPU/GPU 加速效果的粗略参考。
    /// </summary>
    /// <remarks>
    /// <para>注意：单次推理的耗时不一定稳定，且 OpenVINO 首次加载会有编译开销。
    /// 此测试仅用于观察 EP 切换是否生效，不作为严格的性能基准。</para>
    /// </remarks>
    [Fact]
    public async Task ExtractAsync_VGGish_CompareEpPerformance()
    {
        // Arrange
        var modelPath = TryResolveModelPath("VGGish.onnx");
        if (SkipIfModelMissing(modelPath, "VGGish.onnx")) return;

        const int sampleRate = 16000;
        var samples = GenerateSineWave(440.0, sampleRate, 1.0);

        // 启用 OpenVINO 路径
        var enabledOptions = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var enabledAccelerator = new HardwareAccelerator(enabledOptions, NullLogger<HardwareAccelerator>.Instance);
        var enabledExtractor = new DeepFeatureExtractor(
            enabledOptions, enabledAccelerator, NullLogger<DeepFeatureExtractor>.Instance);
        enabledExtractor.LoadModel(modelPath!, DeepModelType.VGGish);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var enabledResult = await enabledExtractor.ExtractAsync(samples, sampleRate);
        sw.Stop();

        // Assert
        enabledResult.IsSuccess.Should().BeTrue();

        _output.WriteLine($"VGGish 推理（ExecutionProvider=OpenVINO）");
        _output.WriteLine($"  实际 EP: {enabledAccelerator.ActiveExecutionProvider}");
        _output.WriteLine($"  耗时: {sw.ElapsedMilliseconds} ms");

        // 关闭加速路径（强制 CPU）
        var disabledOptions = Options.Create(new OnnxModelOptions
        {
            EnableDeepFeatures = false,
            ExecutionProvider = ExecutionProviderMode.CPU
        });
        var disabledAccelerator = new HardwareAccelerator(disabledOptions, NullLogger<HardwareAccelerator>.Instance);
        var disabledExtractor = new DeepFeatureExtractor(
            disabledOptions, disabledAccelerator, NullLogger<DeepFeatureExtractor>.Instance);
        disabledExtractor.LoadModel(modelPath!, DeepModelType.VGGish);

        sw.Restart();
        var disabledResult = await disabledExtractor.ExtractAsync(samples, sampleRate);
        sw.Stop();

        disabledResult.IsSuccess.Should().BeTrue();
        disabledAccelerator.ActiveExecutionProvider.Should().Be("CPU");

        _output.WriteLine($"VGGish 推理（ExecutionProvider=CPU, 强制 CPU）");
        _output.WriteLine($"  耗时: {sw.ElapsedMilliseconds} ms");

        // 两次输出维度应一致（128）
        enabledResult.Value!.Length.Should().Be(128);
        disabledResult.Value!.Length.Should().Be(128);
    }

    /// <summary>
    /// 从测试 bin 目录向上查找仓库根目录，定位 Models/ 下的模型文件。
    /// </summary>
    /// <param name="modelName">模型文件名（如 VGGish.onnx）</param>
    /// <returns>模型绝对路径；未找到时返回 null。</returns>
    /// <remarks>
    /// 测试 bin 路径通常为 <c>src/FindMyFavouriteMusic.Tests/bin/Debug/net10.0-windows/</c>，
    /// 仓库根目录名为 <c>find-my-favourite-music</c>，模型位于其下 <c>Models/</c> 子目录。
    /// </remarks>
    private static string? TryResolveModelPath(string modelName)
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

        var path = Path.Combine(dir.FullName, "Models", modelName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 模型文件不存在时跳过测试：通过输出说明原因并直接返回。
    /// </summary>
    /// <param name="modelPath">解析得到的模型路径（null 表示未找到）</param>
    /// <param name="modelName">模型文件名，用于输出说明</param>
    /// <returns>模型存在返回 false（继续执行测试）；模型缺失返回 true（已跳过）。</returns>
    /// <remarks>
    /// <para>xUnit 2.9.3 的动态跳过 API（<c>Skip.If</c>/<c>Assert.Skip</c>）在当前引用版本中不可用，
    /// 故采用"return + 输出说明"的兼容方案。</para>
    /// <para>当模型存在时（用户环境），测试会实际执行；模型缺失时测试标记为 Passed，输出中说明跳过原因。</para>
    /// </remarks>
    private bool SkipIfModelMissing(string? modelPath, string modelName)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            _output.WriteLine($"跳过测试：未找到模型文件 {modelName}（仓库根/Models/ 下不存在）");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 生成一段指定频率与时长的正弦波 PCM 浮点采样数据，用作测试音频。
    /// </summary>
    /// <param name="frequency">正弦波频率（Hz）</param>
    /// <param name="sampleRate">采样率（Hz）</param>
    /// <param name="durationSeconds">音频时长（秒）</param>
    /// <returns>范围 [-1, 1] 的单声道浮点采样数组</returns>
    private static float[] GenerateSineWave(double frequency, int sampleRate, double durationSeconds)
    {
        var sampleCount = (int)(sampleRate * durationSeconds);
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
        }
        return samples;
    }
}
