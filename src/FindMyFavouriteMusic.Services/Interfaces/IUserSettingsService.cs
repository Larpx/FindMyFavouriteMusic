using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 用户设置持久化服务接口。
/// 负责将 UI 修改的运行时配置（权重、模型路径等）写回用户设置文件。
/// </summary>
public interface IUserSettingsService
{
    /// <summary>保存预测权重配置</summary>
    /// <param name="acousticWeight">声学特征权重（0~1）</param>
    /// <param name="deepWeight">深度特征权重（0~1）</param>
    Task<Result> SavePredictionWeightsAsync(double acousticWeight, double deepWeight);

    /// <summary>保存 ONNX 模型配置</summary>
    /// <param name="enableDeepFeatures">是否启用深度特征</param>
    /// <param name="modelType">模型类型（VGGish 或 MERT）</param>
    /// <param name="vggishModelPath">VGGish 模型文件路径，可为 null</param>
    /// <param name="mertModelPath">MERT 模型文件路径，可为 null</param>
    Task<Result> SaveOnnxModelSettingsAsync(bool enableDeepFeatures, string modelType, string? vggishModelPath, string? mertModelPath);
}
