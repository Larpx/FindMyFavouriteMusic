namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>跨源统一曲目引用（尚未落盘）。</summary>
public sealed class MusicTrackRef
{
    public required string SourceId { get; init; }
    public required string ExternalId { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Artists { get; init; } = [];
    public string? Album { get; init; }
    public int? DurationMs { get; init; }
    /// <summary>网易云等平台的 fee 提示（0 免费 / 1 试听 / 8 VIP 等）。</summary>
    public int? Fee { get; init; }
    public string? RecommendReason { get; init; }
    public string? Algorithm { get; init; }
}
