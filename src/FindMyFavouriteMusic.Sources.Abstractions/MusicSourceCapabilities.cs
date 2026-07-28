namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>音乐源能力声明。</summary>
public sealed class MusicSourceCapabilities
{
    public bool SupportsLogin { get; init; }
    public bool SupportsLikedList { get; init; }
    public bool SupportsDailyRecommend { get; init; }
    public bool SupportsHistoryRecommend { get; init; }
    public bool SupportsStreamingDownload { get; init; }
}
