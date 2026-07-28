namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>音乐源统一错误码（写入 Result.Error 前缀便于 UI 识别）。</summary>
public static class MusicSourceErrorCodes
{
    public const string NotLoggedIn = "SOURCE_NOT_LOGGED_IN";
    public const string VipExpired = "SOURCE_VIP_EXPIRED";
    public const string NoCopyright = "SOURCE_NO_COPYRIGHT";
    public const string Network = "SOURCE_NETWORK";
    public const string RiskControl = "SOURCE_RISK_CONTROL";
    public const string NotSupported = "SOURCE_NOT_SUPPORTED";
    public const string DownloadFailed = "SOURCE_DOWNLOAD_FAILED";
}
