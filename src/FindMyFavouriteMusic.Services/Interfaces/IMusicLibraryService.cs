using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 音乐库管理服务接口
/// </summary>
public interface IMusicLibraryService
{
    /// <summary>扫描目录下支持的音频文件并入库（含特征提取）。</summary>
    Task<Result<IReadOnlyList<SongDto>>> ScanDirectoryAsync(string directoryPath, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>切换喜欢标记并触发画像增量/重建。</summary>
    Task<Result> ToggleLikeAsync(int songId, bool isLiked);

    /// <summary>查询所有已标记喜欢的歌曲。</summary>
    Task<Result<IReadOnlyList<SongDto>>> GetLikedSongsAsync();

    /// <summary>查询音乐库全部歌曲。</summary>
    Task<Result<IReadOnlyList<SongDto>>> GetAllSongsAsync();

    /// <summary>处理单首歌曲（MD5 缓存 / 深度补全 / 新入库）。</summary>
    Task<Result<SongDto>> ProcessSongAsync(string filePath, CancellationToken ct = default);

    /// <summary>打开详情：DB 字段 + 实时读文件标签/封面。</summary>
    Task<Result<SongDetailDto>> GetSongDetailAsync(int songId);

    /// <summary>保存元数据：写回原文件标签，同步 DB 镜像，更新 MD5 保留向量。</summary>
    Task<Result> SaveSongMetadataAsync(SongMetadataUpdateDto update);
}
