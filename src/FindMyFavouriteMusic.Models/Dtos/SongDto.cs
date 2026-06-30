namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 歌曲传输对象，用于 UI 展示
/// </summary>
public class SongDto
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

    /// <summary>是否已提取特征</summary>
    public bool HasFeatures { get; set; }
}
