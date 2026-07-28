using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 用户设置持久化服务接口。
/// 负责将 UI 修改的运行时配置（权重、模型路径等）写回用户设置文件。
/// </summary>
public interface IUserSettingsService
{
    /// <summary>保存预测权重（声学/深度；AcousticOnly 默认 1.0）。</summary>
    Task<Result> SavePredictionWeightsAsync(double acousticWeight, double deepWeight);

    /// <summary>保存预测权重（含仅声学模式权重）。</summary>
    Task<Result> SavePredictionWeightsAsync(double acousticWeight, double deepWeight, double acousticOnlyWeight);

    /// <summary>保存 ONNX 模型配置（含 OpenVINO 缓存目录）。</summary>
    Task<Result> SaveOnnxModelSettingsAsync(
        bool enableDeepFeatures,
        string modelType,
        string? vggishModelPath,
        string? mertModelPath,
        string executionProvider,
        string openVinoDevice,
        string? openVinoCacheDir = null);

    /// <summary>保存上次扫描目录。</summary>
    Task<Result> SaveLastScanDirectoryAsync(string? directoryPath);

    /// <summary>保存扫描相关设置（并发数等）。</summary>
    Task<Result> SaveScanSettingsAsync(int maxConcurrentProcessing, string? lastScanDirectory = null);
}
