using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 预测服务接口
/// </summary>
public interface IPredictionService
{
    Task<Result<PredictionResult>> PredictAsync(string filePath, CancellationToken ct = default);
    Task<Result<PredictionResult>> PredictAsync(int songId, CancellationToken ct = default);

    /// <summary>
    /// 带进度上报的单文件预测，通过 IProgress 报告当前阶段（0-100）。
    /// </summary>
    /// <param name="filePath">音频文件路径</param>
    /// <param name="progress">进度上报（0=开始，25=解码完成，50=声学提取完成，75=深度提取完成，100=预测完成）</param>
    /// <param name="ct">取消令牌</param>
    Task<Result<PredictionResult>> PredictWithProgressAsync(string filePath, IProgress<int>? progress, CancellationToken ct = default);

    /// <summary>
    /// 批量预测：对多个文件依次执行预测，通过 IProgress 报告整体进度（0-100）。
    /// </summary>
    /// <param name="filePaths">音频文件路径列表</param>
    /// <param name="progress">整体进度上报（0=开始，100=全部完成）</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<Result<PredictionResult>>> PredictBatchAsync(IReadOnlyList<string> filePaths, IProgress<int>? progress, CancellationToken ct = default);
}
