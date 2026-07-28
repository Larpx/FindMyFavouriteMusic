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

    /// <summary>更新特征向量与指纹/契约字段（文件内容变化或补全深度时）。</summary>
    Task<Result> UpdateFeaturesAsync(Song song);

    /// <summary>仅更新文件指纹（元数据写回后 MD5 变化，保留向量）。</summary>
    Task<Result> UpdateFingerprintAsync(int id, string fileMd5, long fileSize);

    Task<Result<Song>> GetByIdAsync(int id);
}
