namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;

/// <summary>
/// 网易云 Web API 契约版本与端点清单。
/// 变更路径/加密/响应字段时：更新 <see cref="VersionId"/>（yyyyMMddHHmmss）并同步端点常量。
/// </summary>
public static class NeteaseApiContract
{
    /// <summary>当前契约版本 ID（时间戳）。</summary>
    public const string VersionId = "20260729090000";

    /// <summary>版本说明（探测/回归备注）。</summary>
    public const string VersionNote =
        "QR + account/VIP + liked + daily v2 recommend + player url/v1 (exhigh)";

    public static class Endpoints
    {
        public const string AccountGet = "/api/nuser/account/get";
        public const string VipInfo = "/api/music-vip-membership/front/vip/info";
        public const string QrUnikey = "/api/login/qrcode/unikey";
        public const string QrClientLogin = "/api/login/qrcode/client/login";
        public const string LikedIds = "/api/song/like/get";
        public const string SongDetail = "/weapi/v3/song/detail";
        public const string DailyRecommend = "/weapi/v2/discovery/recommend/songs";
        public const string HistoryRecent = "/weapi/discovery/recommend/songs/history/recent";
        public const string HistoryDetail = "/weapi/discovery/recommend/songs/history/detail";
        public const string PlayerUrlV1 = "/weapi/song/enhance/player/url/v1";
    }
}
