namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

public sealed class QrLoginSession
{
    public required string Key { get; init; }
    /// <summary>用于生成二维码的 URL（如 music.163.com/login?codekey=...）。</summary>
    public required string QrUrl { get; init; }
}

public enum QrLoginStatus
{
    Waiting = 801,
    Scanned = 802,
    Confirmed = 803,
    Expired = 800,
    Unknown = 0
}

public sealed class QrLoginPollResult
{
    public QrLoginStatus Status { get; init; }
    public string? Message { get; init; }
    public string? Nickname { get; init; }
    public string? AvatarUrl { get; init; }
}
