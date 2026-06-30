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
}
