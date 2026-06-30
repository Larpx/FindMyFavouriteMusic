namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;

/// <summary>
/// 歌曲实体，映射数据库 Songs 表
/// </summary>
public class Song
{
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
}
