using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 用户设置持久化服务。
/// <para>策略：将可变配置写入应用目录下的 usersettings.json，</para>
/// <para>该文件在 App 启动时作为附加配置源接入，reloadOnChange=true 保证立即生效。</para>
/// </summary>
public class UserSettingsService : IUserSettingsService
{
    private const string UserSettingsFileName = "usersettings.json";

    private readonly string _settingsFilePath;
    private readonly ILogger<UserSettingsService> _logger;

    public UserSettingsService(
        IConfiguration configuration,
        ILogger<UserSettingsService> logger)
    {
        _logger = logger;
        // 配置文件路径：优先使用应用所在目录，确保可写
        _settingsFilePath = Path.Combine(AppContext.BaseDirectory, UserSettingsFileName);
    }

    /// <inheritdoc/>
    public async Task<Result> SavePredictionWeightsAsync(double acousticWeight, double deepWeight)
    {
        try
        {
            var root = await ReadRootAsync();
            // 确保 Prediction 节点存在
            var prediction = root[nameof(JsonKeys.Prediction)] as JsonObject ?? new JsonObject();
            prediction[nameof(JsonKeys.AcousticWeight)] = acousticWeight;
            prediction[nameof(JsonKeys.DeepWeight)] = deepWeight;
            root[nameof(JsonKeys.Prediction)] = prediction;

            await WriteRootAsync(root);
            _logger.LogInformation("预测权重已保存: Acoustic={Acoustic}, Deep={Deep}",
                acousticWeight, deepWeight);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存预测权重失败");
            return Result.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SaveOnnxModelSettingsAsync(bool enableDeepFeatures, string? vggishModelPath)
    {
        try
        {
            var root = await ReadRootAsync();
            var onnx = root[nameof(JsonKeys.OnnxModel)] as JsonObject ?? new JsonObject();
            onnx[nameof(JsonKeys.EnableDeepFeatures)] = enableDeepFeatures;
            onnx[nameof(JsonKeys.VggishModelPath)] = vggishModelPath ?? string.Empty;
            root[nameof(JsonKeys.OnnxModel)] = onnx;

            await WriteRootAsync(root);
            _logger.LogInformation("ONNX 模型配置已保存: Enable={Enable}, Path={Path}",
                enableDeepFeatures, vggishModelPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 ONNX 模型配置失败");
            return Result.Failure(ex);
        }
    }

    /// <summary>读取现有配置根节点；文件不存在时返回空对象。</summary>
    private async Task<JsonObject> ReadRootAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new JsonObject();
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        var node = await JsonNode.ParseAsync(stream);
        return node as JsonObject ?? new JsonObject();
    }

    /// <summary>将配置根节点写回文件（缩进格式）。</summary>
    private async Task WriteRootAsync(JsonNode root)
    {
        await using var stream = File.Create(_settingsFilePath);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true
        });
        root.WriteTo(writer);
    }

    /// <summary>JSON 键名常量，避免魔法字符串。</summary>
    private static class JsonKeys
    {
        public const string Prediction = "Prediction";
        public const string AcousticWeight = "AcousticWeight";
        public const string DeepWeight = "DeepWeight";
        public const string OnnxModel = "OnnxModel";
        public const string EnableDeepFeatures = "EnableDeepFeatures";
        public const string VggishModelPath = "VggishModelPath";
    }
}
