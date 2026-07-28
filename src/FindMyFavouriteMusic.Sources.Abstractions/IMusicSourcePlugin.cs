using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>
/// 音乐源插件：Local / 网易云 /（预留）QQ 等。
/// 最终通过 <see cref="ResolveAudioAsync"/> 产出可解码本地路径，复用 Core 预测链路。
/// </summary>
public interface IMusicSourcePlugin
{
    string Id { get; }
    string DisplayName { get; }
    MusicSourceCapabilities Capabilities { get; }

    Task<Result<MusicSourceAuthState>> GetAuthStateAsync(CancellationToken ct = default);

    /// <summary>启动二维码登录；不支持时返回 NotSupported。</summary>
    Task<Result<QrLoginSession>> BeginQrLoginAsync(CancellationToken ct = default);

    /// <summary>轮询二维码登录状态。</summary>
    Task<Result<QrLoginPollResult>> PollQrLoginAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>使用原始 Cookie 字符串登录（高级选项）。</summary>
    Task<Result> SignInWithCookieAsync(string cookieHeader, CancellationToken ct = default);

    Task<Result> SignOutAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<MusicTrackRef>>> GetLikedTracksAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<MusicTrackRef>>> GetDailyRecommendAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<string>>> GetHistoryRecommendDatesAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<MusicTrackRef>>> GetHistoryRecommendAsync(string date, CancellationToken ct = default);

    /// <summary>
    /// 解析为本地文件。若 <paramref name="preferTemporary"/> 为 true，下载文件应在 Dispose 时删除。
    /// </summary>
    Task<Result<ResolvedAudio>> ResolveAudioAsync(
        MusicTrackRef track,
        bool preferTemporary = true,
        CancellationToken ct = default);
}
