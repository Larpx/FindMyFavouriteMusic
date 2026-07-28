namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;

/// <summary>
/// 歌曲实体，映射数据库 Songs 表
/// </summary>
public class Song
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>文件绝对路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>歌曲标题</summary>
    public string? Title { get; set; }

    /// <summary>艺术家</summary>
    public string? Artist { get; set; }

    /// <summary>用户是否标记为喜欢</summary>
    public bool IsLiked { get; set; }

    /// <summary>运行时使用的声学特征向量</summary>
    public float[]? AcousticVector { get; set; }

    /// <summary>数据库存储用的声学特征 BLOB</summary>
    public byte[]? AcousticVectorBlob { get; set; }

    /// <summary>运行时使用的深度特征向量</summary>
    public float[]? DeepVector { get; set; }

    /// <summary>数据库存储用的深度特征 BLOB</summary>
    public byte[]? DeepVectorBlob { get; set; }

    /// <summary>文件内容 MD5（小写 hex）</summary>
    public string? FileMd5 { get; set; }

    /// <summary>文件字节大小</summary>
    public long? FileSize { get; set; }

    /// <summary>时长（毫秒）</summary>
    public int? DurationMs { get; set; }

    /// <summary>容器格式（如 Mp3 / Flac）</summary>
    public string? Format { get; set; }

    /// <summary>声学向量维度</summary>
    public int? AcousticDim { get; set; }

    /// <summary>深度模型类型（VGGish / MERT）</summary>
    public string? DeepModelType { get; set; }

    /// <summary>深度向量维度</summary>
    public int? DeepDim { get; set; }

    /// <summary>特征提取完成时间（UTC）</summary>
    public DateTime? FeatureExtractedAt { get; set; }

    /// <summary>专辑（元数据镜像，阶段 D 读写标签时同步）</summary>
    public string? Album { get; set; }

    /// <summary>专辑艺术家</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>流派</summary>
    public string? Genre { get; set; }

    /// <summary>年份</summary>
    public int? Year { get; set; }

    /// <summary>曲目号（如 3 或 3/12）</summary>
    public string? Track { get; set; }

    /// <summary>碟号</summary>
    public string? Disc { get; set; }

    /// <summary>注释</summary>
    public string? Comment { get; set; }

    /// <summary>歌词</summary>
    public string? Lyrics { get; set; }

    /// <summary>音乐源 Id（local / netease 等）；本地扫描默认为 local</summary>
    public string? SourceId { get; set; }

    /// <summary>源内外部 Id（如网易云 songId）</summary>
    public string? ExternalId { get; set; }
}
