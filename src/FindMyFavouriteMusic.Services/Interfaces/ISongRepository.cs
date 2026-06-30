using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 歌曲仓储接口
/// </summary>
public interface ISongRepository
{
    Task<Result<int>> InsertAsync(Song song);
    Task<Result<Song?>> GetByFilePathAsync(string filePath);
    Task<Result<IReadOnlyList<Song>>> GetLikedSongsAsync();
    Task<Result<IReadOnlyList<Song>>> GetAllSongsAsync();
    Task<Result> UpdateLikeStatusAsync(int id, bool isLiked);
    Task<Result> UpdateVectorsAsync(int id, byte[]? acousticVectorBlob, byte[]? deepVectorBlob);
    Task<Result<Song>> GetByIdAsync(int id);
}
