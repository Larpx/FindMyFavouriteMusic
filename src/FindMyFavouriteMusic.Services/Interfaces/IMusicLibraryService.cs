using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 音乐库管理服务接口
/// </summary>
public interface IMusicLibraryService
{
    Task<Result<IReadOnlyList<SongDto>>> ScanDirectoryAsync(string directoryPath, IProgress<int>? progress = null, CancellationToken ct = default);
    Task<Result> ToggleLikeAsync(int songId, bool isLiked);
    Task<Result<IReadOnlyList<SongDto>>> GetLikedSongsAsync();
    Task<Result<IReadOnlyList<SongDto>>> GetAllSongsAsync();
    Task<Result<SongDto>> ProcessSongAsync(string filePath, CancellationToken ct = default);
}
