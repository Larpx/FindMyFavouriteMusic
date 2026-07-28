using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 歌曲仓储接口
/// </summary>
public interface ISongRepository
{
    /// <summary>插入歌曲，返回新 Id。</summary>
    Task<Result<int>> InsertAsync(Song song);

    /// <summary>按绝对路径查询（不存在时 Value 为 null）。</summary>
    Task<Result<Song?>> GetByFilePathAsync(string filePath);

    /// <summary>查询所有喜欢歌曲。</summary>
    Task<Result<IReadOnlyList<Song>>> GetLikedSongsAsync();

    /// <summary>查询全部歌曲。</summary>
    Task<Result<IReadOnlyList<Song>>> GetAllSongsAsync();

    /// <summary>更新喜欢标记。</summary>
    Task<Result> UpdateLikeStatusAsync(int id, bool isLiked);

    /// <summary>仅更新声学/深度向量 BLOB（不改指纹）。</summary>
    Task<Result> UpdateVectorsAsync(int id, byte[]? acousticVectorBlob, byte[]? deepVectorBlob);

    /// <summary>更新特征向量与指纹/契约字段（文件内容变化或补全深度时）。</summary>
    Task<Result> UpdateFeaturesAsync(Song song);

    /// <summary>仅更新文件指纹（元数据写回后 MD5 变化，保留向量）。</summary>
    Task<Result> UpdateFingerprintAsync(int id, string fileMd5, long fileSize);

    /// <summary>更新元数据镜像字段（不含向量）。</summary>
    Task<Result> UpdateMetadataAsync(Song song);

    /// <summary>按 Id 查询；不存在返回 Failure。</summary>
    Task<Result<Song>> GetByIdAsync(int id);

    /// <summary>按音乐源外部 Id 查询（不存在时 Value 为 null）。</summary>
    Task<Result<Song?>> GetBySourceExternalIdAsync(string sourceId, string externalId);

    /// <summary>按标题+艺人模糊匹配本地曲（用于红心导入对齐）。</summary>
    Task<Result<Song?>> FindByTitleArtistAsync(string title, string? artist);

    /// <summary>更新音乐源绑定字段。</summary>
    Task<Result> UpdateSourceAsync(int id, string sourceId, string externalId);
}
