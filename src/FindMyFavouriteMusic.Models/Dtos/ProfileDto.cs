namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 用户画像传输对象
/// </summary>
public class ProfileDto
{
    /// <summary>喜欢的歌曲数量</summary>
    public int LikedSongCount { get; set; }

    /// <summary>画像最后更新时间</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>是否有深度特征画像</summary>
    public bool HasDeepProfile { get; set; }
}
