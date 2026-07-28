namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>音乐源登录与会员状态。</summary>
public sealed class MusicSourceAuthState
{
    public bool IsAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? DisplayName { get; init; }
    public bool IsVip { get; init; }
    public int? VipLevel { get; init; }
    /// <summary>会员到期（本地时区展示用，来源为服务端毫秒时间戳）。</summary>
    public DateTimeOffset? VipExpireAt { get; init; }
    public string? Message { get; init; }
}
