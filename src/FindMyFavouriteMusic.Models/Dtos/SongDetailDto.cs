namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 歌曲详情（实时从文件标签读取 + DB 只读技术字段）。
/// </summary>
public class SongDetailDto
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;

    // —— 可编辑（写回文件标签）——
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public string? Track { get; set; }
    public string? Disc { get; set; }
    public string? Comment { get; set; }
    public string? Lyrics { get; set; }

    /// <summary>内嵌封面原始字节（JPEG/PNG 等）；null 表示无封面。</summary>
    public byte[]? CoverImageData { get; set; }

    /// <summary>封面 MIME（如 image/jpeg）。</summary>
    public string? CoverMimeType { get; set; }

    // —— 只读 ——
    public string? Format { get; set; }
    public long? FileSize { get; set; }
    public int? DurationMs { get; set; }
    public string? FileMd5 { get; set; }
    public bool IsLiked { get; set; }
    public bool HasAcousticFeatures { get; set; }
    public bool HasDeepFeatures { get; set; }
    public int? AcousticDim { get; set; }
    public int? DeepDim { get; set; }
    public string? DeepModelType { get; set; }
    public bool IsReadOnlyFile { get; set; }
}

/// <summary>
/// 保存元数据时的可写字段（直接写原文件）。
/// </summary>
public class SongMetadataUpdateDto
{
    public int SongId { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public string? Track { get; set; }
    public string? Disc { get; set; }
    public string? Comment { get; set; }
    public string? Lyrics { get; set; }

    /// <summary>新封面数据；null 且 <see cref="ClearCover"/>=false 表示保持原封面。</summary>
    public byte[]? CoverImageData { get; set; }

    public string? CoverMimeType { get; set; }

    /// <summary>为 true 时清除文件内嵌封面。</summary>
    public bool ClearCover { get; set; }

    /// <summary>为 true 时用 CoverImageData 替换封面。</summary>
    public bool ReplaceCover { get; set; }
}
