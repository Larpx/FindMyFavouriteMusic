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

    /// <summary>打开详情：DB 字段 + 实时读文件标签/封面。</summary>
    Task<Result<SongDetailDto>> GetSongDetailAsync(int songId);

    /// <summary>保存元数据：写回原文件标签，同步 DB 镜像，更新 MD5 保留向量。</summary>
    Task<Result> SaveSongMetadataAsync(SongMetadataUpdateDto update);
}
