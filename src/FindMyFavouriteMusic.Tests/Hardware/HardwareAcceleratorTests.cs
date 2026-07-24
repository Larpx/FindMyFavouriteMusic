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
        var options = Options.Create(new OnnxModelOptions { PreferNpu = true });
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
        var options = Options.Create(new OnnxModelOptions { PreferNpu = true });
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
    /// 当 <see cref="OnnxModelOptions.PreferNpu"/> = false 时，无论是否检测到 NPU，
    /// <see cref="IHardwareAccelerator.ConfigureSessionOptions"/> 都应返回 Failure 且 EP 保持 CPU。
    /// </summary>
    [Fact]
    public void ConfigureSessionOptions_PreferNpuFalse_ReturnsFailureAndKeepsCpu()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions { PreferNpu = false });
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
    /// 此测试不强制要求成功启用 DirectML（依赖运行时驱动与 DirectML 运行时），
    /// 仅验证调用安全性与状态一致性。
    /// </remarks>
    [Fact]
    public void ConfigureSessionOptions_AlwaysSafe_NoExceptionAndStateConsistent()
    {
        // Arrange
        var options = Options.Create(new OnnxModelOptions { PreferNpu = true });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);
        var sessionOptions = new SessionOptions();

        // Act
        var result = accelerator.ConfigureSessionOptions(sessionOptions);

        // Assert
        // 不抛异常已隐含在 Act 中；状态必须与返回结果一致
        if (result.IsSuccess)
        {
            accelerator.ActiveExecutionProvider.Should().Be("DirectML");
            _output.WriteLine("DirectML EP 配置成功，将尝试使用 NPU/GPU 加速");
        }
        else
        {
            accelerator.ActiveExecutionProvider.Should().Be("CPU");
            _output.WriteLine($"DirectML EP 不可用，回退 CPU: {result.Error}");
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
        var options = Options.Create(new OnnxModelOptions { PreferNpu = true });
        var accelerator = new HardwareAccelerator(options, NullLogger<HardwareAccelerator>.Instance);

        // Act & Assert：连续三次调用均不应抛异常
        for (var i = 0; i < 3; i++)
        {
            var act = () => accelerator.ConfigureSessionOptions(new SessionOptions());
            act.Should().NotThrow();
        }

        // 最终状态应为 CPU 或 DirectML 之一
        accelerator.ActiveExecutionProvider.Should().BeOneOf("CPU", "DirectML");
    }
}
