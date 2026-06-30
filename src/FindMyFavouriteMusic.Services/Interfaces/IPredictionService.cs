using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Models.Results;

namespace FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 预测服务接口
/// </summary>
public interface IPredictionService
{
    Task<Result<PredictionResult>> PredictAsync(string filePath, CancellationToken ct = default);
    Task<Result<PredictionResult>> PredictAsync(int songId, CancellationToken ct = default);
}
