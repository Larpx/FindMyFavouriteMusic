using System.Text.Json;
using System.Text.Json.Nodes;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 用户设置持久化服务：写入 AppData 下的 usersettings.json。
/// </summary>
/// <remarks>
/// 写文件经 <see cref="SemaphoreSlim"/> 串行化，并以临时文件 + 原子替换避免半写入损坏。
/// </remarks>
public class UserSettingsService : IUserSettingsService, IDisposable
{
    private readonly string _settingsFilePath;
    private readonly ILogger<UserSettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public UserSettingsService(ILogger<UserSettingsService> logger)
        : this(logger, UserSettingsPaths.GetUserSettingsFilePath())
    {
    }

    /// <summary>测试或自定义路径用构造。</summary>
    public UserSettingsService(ILogger<UserSettingsService> logger, string settingsFilePath)
    {
        _logger = logger;
        _settingsFilePath = settingsFilePath;
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 仅默认 AppData 路径时尝试迁移旧版配置
        if (string.Equals(_settingsFilePath, UserSettingsPaths.GetUserSettingsFilePath(), StringComparison.OrdinalIgnoreCase))
        {
            TryMigrateFromLegacy();
        }
    }

    /// <inheritdoc/>
    public Task<Result> SavePredictionWeightsAsync(double acousticWeight, double deepWeight) =>
        SavePredictionWeightsAsync(acousticWeight, deepWeight, acousticOnlyWeight: 1.0);

    /// <inheritdoc/>
    public async Task<Result> SavePredictionWeightsAsync(
        double acousticWeight, double deepWeight, double acousticOnlyWeight)
    {
        if (!IsWeightValid(acousticWeight) || !IsWeightValid(deepWeight) || !IsWeightValid(acousticOnlyWeight))
        {
            return Result.Failure("权重值必须在 0~1 范围内");
        }

        if (Math.Abs(acousticWeight + deepWeight - 1.0) > 0.05)
        {
            return Result.Failure($"声学权重与深度权重之和应为 1.0，当前为 {acousticWeight + deepWeight:F2}");
        }

        return await MutateAsync(root =>
        {
            var prediction = root[JsonKeys.Prediction] as JsonObject ?? new JsonObject();
            prediction[JsonKeys.AcousticWeight] = acousticWeight;
            prediction[JsonKeys.DeepWeight] = deepWeight;
            prediction[JsonKeys.AcousticOnlyWeight] = acousticOnlyWeight;
            root[JsonKeys.Prediction] = prediction;
        }, $"预测权重已保存: Acoustic={acousticWeight}, Deep={deepWeight}, AcousticOnly={acousticOnlyWeight}");
    }

    /// <inheritdoc/>
    public async Task<Result> SaveOnnxModelSettingsAsync(
        bool enableDeepFeatures,
        string modelType,
        string? vggishModelPath,
        string? mertModelPath,
        string executionProvider,
        string openVinoDevice,
        string? openVinoCacheDir = null)
    {
        if (!Enum.TryParse<DeepModelType>(modelType, ignoreCase: true, out _))
        {
            return Result.Failure($"无效的模型类型: {modelType}");
        }

        if (!Enum.TryParse<ExecutionProviderMode>(executionProvider, ignoreCase: true, out _))
        {
            return Result.Failure($"无效的 ExecutionProvider: {executionProvider}");
        }

        if (!Enum.TryParse<OpenVinoDeviceType>(openVinoDevice, ignoreCase: true, out _))
        {
            return Result.Failure($"无效的 OpenVinoDevice: {openVinoDevice}");
        }

        return await MutateAsync(root =>
        {
            var onnx = root[JsonKeys.OnnxModel] as JsonObject ?? new JsonObject();
            onnx[JsonKeys.EnableDeepFeatures] = enableDeepFeatures;
            onnx[JsonKeys.ModelType] = modelType;
            onnx[JsonKeys.VggishModelPath] = vggishModelPath ?? string.Empty;
            onnx[JsonKeys.MertModelPath] = mertModelPath ?? string.Empty;
            onnx[JsonKeys.ExecutionProvider] = executionProvider;
            onnx[JsonKeys.OpenVinoDevice] = openVinoDevice;
            if (openVinoCacheDir is not null)
            {
                onnx[JsonKeys.OpenVinoCacheDir] = openVinoCacheDir;
            }

            root[JsonKeys.OnnxModel] = onnx;
        }, $"ONNX 模型配置已保存: Type={modelType}, EP={executionProvider}");
    }

    /// <inheritdoc/>
    public async Task<Result> SaveLastScanDirectoryAsync(string? directoryPath)
    {
        return await MutateAsync(root =>
        {
            var scan = root[JsonKeys.Scan] as JsonObject ?? new JsonObject();
            scan[JsonKeys.LastScanDirectory] =
                string.IsNullOrWhiteSpace(directoryPath) ? null : directoryPath;
            root[JsonKeys.Scan] = scan;
        }, $"扫描目录已保存: {directoryPath ?? "(空)"}");
    }

    /// <inheritdoc/>
    public async Task<Result> SaveScanSettingsAsync(int maxConcurrentProcessing, string? lastScanDirectory = null)
    {
        if (maxConcurrentProcessing is < 1 or > 32)
        {
            return Result.Failure("MaxConcurrentProcessing 必须在 1~32 之间");
        }

        return await MutateAsync(root =>
        {
            var scan = root[JsonKeys.Scan] as JsonObject ?? new JsonObject();
            scan[JsonKeys.MaxConcurrentProcessing] = maxConcurrentProcessing;
            if (lastScanDirectory is not null)
            {
                scan[JsonKeys.LastScanDirectory] =
                    string.IsNullOrWhiteSpace(lastScanDirectory) ? null : lastScanDirectory;
            }

            root[JsonKeys.Scan] = scan;
        }, $"扫描配置已保存: MaxConcurrent={maxConcurrentProcessing}");
    }

    /// <inheritdoc/>
    public void Dispose() => _writeLock.Dispose();

    private async Task<Result> MutateAsync(Action<JsonObject> mutate, string successLog)
    {
        await _writeLock.WaitAsync();
        try
        {
            var root = await ReadRootAsync();
            mutate(root);
            await WriteRootAtomicAsync(root);
            _logger.LogInformation("{Message}", successLog);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存用户设置失败");
            return Result.Failure(ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

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

    private async Task WriteRootAtomicAsync(JsonNode root)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = _settingsFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            root.WriteTo(writer);
        }

        // 原子替换：目标不存在时 Move 即可；已存在则覆盖替换
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    private void TryMigrateFromLegacy()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                return;
            }

            var legacy = UserSettingsPaths.GetLegacySettingsFilePath();
            if (!File.Exists(legacy))
            {
                return;
            }

            File.Copy(legacy, _settingsFilePath, overwrite: false);
            _logger.LogInformation("已从应用目录迁移 usersettings.json 到 AppData: {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "迁移旧版 usersettings.json 失败，将使用空配置");
        }
    }

    private static bool IsWeightValid(double weight) => weight is >= 0 and <= 1;

    private static class JsonKeys
    {
        public const string Prediction = "Prediction";
        public const string AcousticWeight = "AcousticWeight";
        public const string DeepWeight = "DeepWeight";
        public const string AcousticOnlyWeight = "AcousticOnlyWeight";
        public const string OnnxModel = "OnnxModel";
        public const string EnableDeepFeatures = "EnableDeepFeatures";
        public const string ModelType = "ModelType";
        public const string VggishModelPath = "VggishModelPath";
        public const string MertModelPath = "MertModelPath";
        public const string ExecutionProvider = "ExecutionProvider";
        public const string OpenVinoDevice = "OpenVinoDevice";
        public const string OpenVinoCacheDir = "OpenVinoCacheDir";
        public const string Scan = "Scan";
        public const string LastScanDirectory = "LastScanDirectory";
        public const string MaxConcurrentProcessing = "MaxConcurrentProcessing";
    }
}
