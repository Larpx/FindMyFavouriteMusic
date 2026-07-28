namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 歌曲详情（实时从文件标签读取 + DB 只读技术字段）。
/// </summary>
/// <remarks>
/// 封面仅存在于音频文件内嵌 picture，不进入数据库；打开详情时由 TagLib 实时读取。
/// </remarks>
public class SongDetailDto
{
    /// <summary>数据库歌曲 Id。</summary>
    public int Id { get; set; }

    /// <summary>文件绝对路径。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>标题（可编辑，写回标签）。</summary>
    public string? Title { get; set; }

    /// <summary>艺术家 / 表演者（可编辑）。</summary>
    public string? Artist { get; set; }

    /// <summary>专辑（可编辑）。</summary>
    public string? Album { get; set; }

    /// <summary>专辑艺术家（可编辑）。</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>流派（可编辑）。</summary>
    public string? Genre { get; set; }

    /// <summary>年份（可编辑）。</summary>
    public int? Year { get; set; }

    /// <summary>曲目号，如 <c>3</c> 或 <c>3/12</c>（可编辑）。</summary>
    public string? Track { get; set; }

    /// <summary>碟号（可编辑）。</summary>
    public string? Disc { get; set; }

    /// <summary>注释（可编辑）。</summary>
    public string? Comment { get; set; }

    /// <summary>歌词（可编辑）。</summary>
    public string? Lyrics { get; set; }

    /// <summary>内嵌封面原始字节（JPEG/PNG 等）；null 表示无封面。</summary>
    public byte[]? CoverImageData { get; set; }

    /// <summary>封面 MIME（如 image/jpeg）。</summary>
    public string? CoverMimeType { get; set; }

    /// <summary>容器格式（只读）。</summary>
    public string? Format { get; set; }

    /// <summary>文件字节大小（只读）。</summary>
    public long? FileSize { get; set; }

    /// <summary>时长毫秒（只读）。</summary>
    public int? DurationMs { get; set; }

    /// <summary>文件内容 MD5（只读，缓存指纹）。</summary>
    public string? FileMd5 { get; set; }

    /// <summary>是否喜欢（只读展示；喜欢切换仍在列表操作）。</summary>
    public bool IsLiked { get; set; }

    /// <summary>是否已有声学特征（只读）。</summary>
    public bool HasAcousticFeatures { get; set; }

    /// <summary>是否已有深度特征（只读）。</summary>
    public bool HasDeepFeatures { get; set; }

    /// <summary>声学向量维度（只读）。</summary>
    public int? AcousticDim { get; set; }

    /// <summary>深度向量维度（只读）。</summary>
    public int? DeepDim { get; set; }

    /// <summary>深度模型类型名，如 VGGish / MERT（只读）。</summary>
    public string? DeepModelType { get; set; }

    /// <summary>文件系统只读时为 true，UI 应禁用保存。</summary>
    public bool IsReadOnlyFile { get; set; }
}

/// <summary>
/// 保存元数据时的可写字段（直接写原文件）。
/// </summary>
public class SongMetadataUpdateDto
{
    /// <summary>目标歌曲 Id。</summary>
    public int SongId { get; set; }

    /// <summary>标题。</summary>
    public string? Title { get; set; }

    /// <summary>艺术家。</summary>
    public string? Artist { get; set; }

    /// <summary>专辑。</summary>
    public string? Album { get; set; }

    /// <summary>专辑艺术家。</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>流派。</summary>
    public string? Genre { get; set; }

    /// <summary>年份。</summary>
    public int? Year { get; set; }

    /// <summary>曲目号。</summary>
    public string? Track { get; set; }

    /// <summary>碟号。</summary>
    public string? Disc { get; set; }

    /// <summary>注释。</summary>
    public string? Comment { get; set; }

    /// <summary>歌词。</summary>
    public string? Lyrics { get; set; }

    /// <summary>新封面数据；null 且 <see cref="ClearCover"/>=false 表示保持原封面。</summary>
    public byte[]? CoverImageData { get; set; }

    /// <summary>封面 MIME。</summary>
    public string? CoverMimeType { get; set; }

    /// <summary>为 true 时清除文件内嵌封面。</summary>
    public bool ClearCover { get; set; }

    /// <summary>为 true 时用 <see cref="CoverImageData"/> 替换封面。</summary>
    public bool ReplaceCover { get; set; }
}
