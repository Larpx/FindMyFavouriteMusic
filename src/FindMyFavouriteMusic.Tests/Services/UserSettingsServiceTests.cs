using System.Text.Json.Nodes;
using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// UserSettingsService 持久化与校验测试（使用临时目录，不污染真实 AppData）。
/// </summary>
public class UserSettingsServiceTests
{
    [Fact]
    public async Task SavePredictionWeights_PersistsAcousticOnlyWeight()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"uss_{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(tempDir, "usersettings.json");
        var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), settingsPath);

        try
        {
            var invalid = await service.SavePredictionWeightsAsync(0.3, 0.3, 1.0);
            invalid.IsSuccess.Should().BeFalse();

            var invalidOnly = await service.SavePredictionWeightsAsync(0.4, 0.6, 1.5);
            invalidOnly.IsSuccess.Should().BeFalse();

            var ok = await service.SavePredictionWeightsAsync(0.4, 0.6, 1.0);
            ok.IsSuccess.Should().BeTrue();

            File.Exists(settingsPath).Should().BeTrue();
            var root = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
            root["Prediction"]!["AcousticOnlyWeight"]!.GetValue<double>().Should().Be(1.0);
            root["Prediction"]!["AcousticWeight"]!.GetValue<double>().Should().Be(0.4);
        }
        finally
        {
            service.Dispose();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveScanSettings_RejectsOutOfRangeConcurrency()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"uss_{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(tempDir, "usersettings.json");
        using var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), settingsPath);

        var result = await service.SaveScanSettingsAsync(0);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SaveOnnxModelSettings_PersistsCacheDir_Atomically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"uss_{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(tempDir, "usersettings.json");
        using var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), settingsPath);

        try
        {
            var result = await service.SaveOnnxModelSettingsAsync(
                true, "MERT", null, "Models/x.onnx", "OpenVINO", "GPU", "./cache-test");
            result.IsSuccess.Should().BeTrue();

            File.Exists(settingsPath).Should().BeTrue();
            File.Exists(settingsPath + ".tmp").Should().BeFalse();
            var root = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
            root["OnnxModel"]!["OpenVinoCacheDir"]!.GetValue<string>().Should().Be("./cache-test");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
