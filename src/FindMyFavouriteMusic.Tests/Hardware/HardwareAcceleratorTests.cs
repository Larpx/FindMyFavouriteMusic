using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Hardware;

/// <summary>
/// <see cref="HardwareAccelerator"/> 单元测试：验证 NPU 检测与 EP 配置的健壮性。
/// </summary>
/// <remarks>
/// <para>v2.0 起仅测试 OpenVINO + CPU 双 EP 架构（DirectML 已移除）。</para>
/// <para>这些测试不依赖任何 ONNX 模型文件，可在任意 Windows 环境运行。</para>
/// <para>测试关注点：</para>
/// <para>1. 构造与检测不抛异常（即使 WMI 服务不可用）；</para>
/// <para>2. <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 在各种条件下均安全返回；</para>
/// <para>3. <see cref="IHardwareAccelerator.ActiveExecutionProvider"/> 状态正确反映配置结果。</para>
/// </remarks>
public class HardwareAcceleratorTests
{
    private readonly ITestOutputHelper _output;

    public HardwareAcceleratorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 构造时执行 NPU 检测，无论 WMI 是否可用都不应抛异常。
    /// </summary>
    [Fact]
    public void Constructor_DetectNpu_DoesNotThrow()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions { ExecutionProvider = ExecutionProviderMode.OpenVINO });
        var logger = NullLogger<HardwareAccelerator>.Instance;

        // Act
        var act = () => new HardwareAccelerator(options, logger);

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// 构造完成后，<see cref="IHardwareAccelerator.IsNpuAvailable"/> 与
    /// <see cref="IHardwareAccelerator.NpuDeviceName"/> 应处于一致状态，
    /// 且 <see cref="IHardwareAccelerator.ActiveExecutionProvider"/> 默认为 CPU。
    /// </summary>
    [Fact]
    public void Constructor_DefaultState_IsConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions { ExecutionProvider = ExecutionProviderMode.OpenVINO });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act
        var isNpuAvailable = accelerator.IsNpuAvailable;
        var npuName = accelerator.NpuDeviceName;
        var activeEp = accelerator.ActiveExecutionProvider;

        // Assert
        // 默认 EP 必须是 CPU（还未调用 ConfigureSessionOptions）
        activeEp.Should().Be("CPU");
        // 检测到 NPU 时设备名必须非空；未检测到时必须为 null
        if (isNpuAvailable)
        {
            npuName.Should().NotBeNullOrEmpty();
        }
        else
        {
            npuName.Should().BeNull();
        }

        _output.WriteLine($"NPU 检测结果: Available={isNpuAvailable}, Device={npuName ?? "(未检测到)"}");
    }

    /// <summary>
    /// 当 <see cref="OnnxModelOptions.ExecutionProvider"/> = CPU 时，
    /// <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 都应返回 Failure 且 EP 保持 CPU。
    /// </summary>
    [Fact]
    public void ConfigureSessionOptions_ExecutionProviderCpu_ReturnsFailureAndKeepsCpu()
    {
        // Arrange：显式 CPU EP
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.CPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var sessionOptions = new SessionOptions();

        // Act
        var result = accelerator.ConfigureSessionOptions(sessionOptions);

        // Assert
        result.IsSuccess.Should().BeFalse();
        accelerator.ActiveExecutionProvider.Should().Be("CPU");
    }

    /// <summary>
    /// 无论检测结果如何，<see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 都不应抛异常，
    /// 且 <see cref="IHardwareAccelerator.ActiveExecutionProvider"/> 应反映实际配置结果。
    /// </summary>
    /// <remarks>
    /// 此测试不强制要求成功启用 OpenVINO（依赖运行时驱动与 OpenVINO native 库），
    /// 仅验证调用安全性与状态一致性。
    /// </remarks>
    [Fact]
    public void ConfigureSessionOptions_AlwaysSafe_NoExceptionAndStateConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var sessionOptions = new SessionOptions();

        // Act
        var result = accelerator.ConfigureSessionOptions(sessionOptions);

        // Assert
        // 不抛异常已隐含在 Act 中；状态必须与返回结果一致
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(GPU)");
            _output.WriteLine("OpenVINO(GPU) EP 配置成功，将尝试使用 GPU 加速");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"OpenVINO(GPU) EP 不可用，回退 CPU: {result.Error}");
        }
    }

    /// <summary>
    /// 多次调用 <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 应保持稳定，
    /// 每次都基于当前检测结果重新决策，不产生累积副作用。
    /// </summary>
    [Fact]
    public void ConfigureSessionOptions_MultipleCalls_AreStable()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act & Assert：连续三次调用均不应抛异常
        for (var i = 0; i < 3; i++)
        {
            var act = () => accelerator.ConfigureSessionOptions(new SessionOptions());
            act.Should().NotThrow();
        }

        // 最终状态应为 CPU 或 OpenVINO(GPU) 之一
        accelerator.ActiveExecutionProvider.Should().BeOneOf("CPU", "OpenVINO(GPU)");
    }

    /// <summary>
    /// <see cref="IHardwareAccelerator.MarkCpuFallbackActive"/> 在 CPU EP 下调用应安全无副作用，
    /// 不抛异常且 EP 保持 CPU。
    /// </summary>
    [Fact]
    public void MarkCpuFallbackActive_AlreadyCpu_IsSafeAndIdempotent()
    {
        // Arrange：未调用 ConfigureSessionOptions 前 EP 默认为 CPU
        var options = Options.Create(new OnnxModelOptions { ExecutionProvider = ExecutionProviderMode.OpenVINO });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        accelerator.ActiveExecutionProvider.Should().Be("CPU");

        // Act & Assert：多次调用均不抛异常，EP 保持 CPU
        var act = () =>
        {
            accelerator.MarkCpuFallbackActive();
            accelerator.MarkCpuFallbackActive();
        };
        act.Should().NotThrow();
        accelerator.ActiveExecutionProvider.Should().Be("CPU");
    }

    /// <summary>
    /// 当 OpenVINO EP 可用时，<see cref="IHardwareAccelerator.MarkCpuFallbackActive"/> 应将 EP 从 OpenVINO 切换为 CPU；
    /// 若环境不支持 OpenVINO，则跳过切换验证。
    /// </summary>
    /// <remarks>
    /// 此测试验证提取器推理失败回退 CPU 时，HardwareAccelerator 状态能正确更新，
    /// 确保设置页显示与实际生效的 EP 一致。
    /// </remarks>
    [Fact]
    public void MarkCpuFallbackActive_FromOpenVino_SetsToCpu_WhenOpenVinoAvailable()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // 尝试启用 OpenVINO EP
        var epResult = accelerator.ConfigureSessionOptions(new SessionOptions());
        if (!epResult.IsSuccess)
        {
            _output.WriteLine("跳过：当前环境 OpenVINO 不可用，无法测试从 OpenVINO 回退 CPU 的状态切换");
            return;
        }

        // Assert 初始状态：OpenVINO 已启用
        accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(GPU)");

        // Act：调用 MarkCpuFallbackActive 模拟推理失败后的回退
        accelerator.MarkCpuFallbackActive();

        // Assert：EP 应切换为 CPU
        accelerator.ActiveExecutionProvider.Should().Be("CPU");

        // 幂等：再次调用不应抛异常，EP 保持 CPU
        accelerator.MarkCpuFallbackActive();
        accelerator.ActiveExecutionProvider.Should().Be("CPU");

        _output.WriteLine("OpenVINO(GPU) → CPU 回退标记验证通过");
    }

    // ===== OpenVINO EP 设备类型测试 =====

    /// <summary>
    /// 验证 <see cref="ExecutionProviderMode.OpenVINO"/> + <see cref="OpenVinoDeviceType.NPU"/>
    /// 调用 <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 的安全性与状态一致性。
    /// </summary>
    /// <remarks>
    /// 此测试不强制要求 OpenVINO EP 注册成功（依赖 OpenVINO native 库是否已加载），
    /// 仅验证：1) 调用不抛异常；2) 状态与返回结果一致。
    /// 若 ModuleInitializer 已通过环境变量加载 OpenVINO native 库，应返回 Success 且 EP="OpenVINO(NPU)"；
    /// 否则返回 Failure 且 EP="CPU"。
    /// </remarks>
    [Fact]
    public void ConfigureSessionOptions_OpenVinoNpu_SafeAndStateConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.NPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act
        var result = accelerator.ConfigureSessionOptions(new SessionOptions());

        // Assert：状态必须与返回结果一致
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(NPU)");
            _output.WriteLine("OpenVINO(NPU) EP 配置成功");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"OpenVINO(NPU) EP 不可用，回退 CPU: {result.Error}");
        }
    }

    /// <summary>
    /// 验证 <see cref="ExecutionProviderMode.OpenVINO"/> + <see cref="OpenVinoDeviceType.GPU"/> 的调用安全性。
    /// </summary>
    [Fact]
    public void ConfigureSessionOptions_OpenVinoGpu_SafeAndStateConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.GPU
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act
        var result = accelerator.ConfigureSessionOptions(new SessionOptions());

        // Assert
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(GPU)");
            _output.WriteLine("OpenVINO(GPU) EP 配置成功");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"OpenVINO(GPU) EP 不可用，回退 CPU: {result.Error}");
        }
    }

    /// <summary>
    /// 验证 <see cref="ExecutionProviderMode.OpenVINO"/> + <see cref="OpenVinoDeviceType.AUTO"/> 的调用安全性。
    /// </summary>
    [Fact]
    public void ConfigureSessionOptions_OpenVinoAuto_SafeAndStateConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.AUTO
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act
        var result = accelerator.ConfigureSessionOptions(new SessionOptions());

        // Assert
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(AUTO)");
            _output.WriteLine("OpenVINO(AUTO) EP 配置成功");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"OpenVINO(AUTO) EP 不可用，回退 CPU: {result.Error}");
        }
    }

    /// <summary>
    /// 验证设置 <see cref="OnnxModelOptions.OpenVinoCacheDir"/> 时，
    /// <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 不抛异常且状态一致。
    /// </summary>
    /// <remarks>
    /// 缓存目录通过 <see cref="SessionOptions.AddSessionConfigEntry"/> 传入，
    /// 该方法本身不抛异常（即使目录不存在），由 OpenVINO EP 在编译时按需创建。
    /// 此测试验证缓存配置与 EP 注册的组合调用安全。
    /// </remarks>
    [Fact]
    public void ConfigureSessionOptions_OpenVinoWithCacheDir_SafeAndStateConsistent()
    {
        // Arrange：使用一个临时目录作为缓存路径
        var cacheDir = Path.Combine(Path.GetTempPath(), "fmm-openvino-cache-test");
        var options = Options.Create(new OnnxModelOptions
        {
            ExecutionProvider = ExecutionProviderMode.OpenVINO,
            OpenVinoDevice = OpenVinoDeviceType.NPU,
            OpenVinoCacheDir = cacheDir
        });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act
        var result = accelerator.ConfigureSessionOptions(new SessionOptions());

        // Assert：缓存配置不影响 EP 注册成功与否的判定逻辑
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("OpenVINO(NPU)");
            _output.WriteLine($"OpenVINO EP 配置成功（缓存目录: {cacheDir}）");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"OpenVINO EP 不可用（缓存目录配置已尝试）: {result.Error}");
        }
    }
}
