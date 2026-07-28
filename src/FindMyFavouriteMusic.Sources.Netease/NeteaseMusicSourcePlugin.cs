using System.Globalization;
using System.Text.Json;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;

/// <summary>网易云音乐源插件（Web weapi）。</summary>
public sealed class NeteaseMusicSourcePlugin : IMusicSourcePlugin
{
    private readonly NeteaseApiClient _api;
    private readonly NeteaseOptions _options;
    private readonly ILogger<NeteaseMusicSourcePlugin> _logger;

    public NeteaseMusicSourcePlugin(
        NeteaseApiClient api,
        IOptions<NeteaseOptions> options,
        ILogger<NeteaseMusicSourcePlugin> logger)
    {
        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    public string Id => MusicSourceIds.Netease;
    public string DisplayName => "网易云音乐";

    public MusicSourceCapabilities Capabilities { get; } = new()
    {
        SupportsLogin = true,
        SupportsLikedList = true,
        SupportsDailyRecommend = true,
        SupportsHistoryRecommend = true,
        SupportsStreamingDownload = true
    };

    public async Task<Result<MusicSourceAuthState>> GetAuthStateAsync(CancellationToken ct = default)
    {
        try
        {
            using var accountDoc = await _api.GetApiAsync("/api/nuser/account/get", ct);
            var root = accountDoc.RootElement;
            if (root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
            {
                var uid = account.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
                string? nick = null;
                if (root.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object
                    && profile.TryGetProperty("nickname", out var nickEl))
                {
                    nick = nickEl.GetString();
                }

                DateTimeOffset? vipExpire = null;
                var vipLevel = 0;
                var isVip = false;
                try
                {
                    using var vipDoc = await _api.GetApiAsync("/api/music-vip-membership/front/vip/info", ct);
                    var vipRoot = vipDoc.RootElement;
                    if (vipRoot.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() == 200
                        && vipRoot.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("redVipLevel", out var lv))
                        {
                            vipLevel = lv.GetInt32();
                        }

                        if (data.TryGetProperty("associator", out var assoc)
                            && assoc.TryGetProperty("expireTime", out var exp)
                            && exp.TryGetInt64(out var ms) && ms > 0)
                        {
                            vipExpire = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                            isVip = vipExpire > DateTimeOffset.UtcNow;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "读取 VIP 信息失败（忽略）");
                }

                return Result<MusicSourceAuthState>.Success(new MusicSourceAuthState
                {
                    IsAuthenticated = true,
                    UserId = uid,
                    DisplayName = nick,
                    IsVip = isVip,
                    VipLevel = vipLevel,
                    VipExpireAt = vipExpire
                });
            }

            return Result<MusicSourceAuthState>.Success(new MusicSourceAuthState
            {
                IsAuthenticated = false,
                Message = "未登录"
            });
        }
        catch (Exception ex)
        {
            return Result<MusicSourceAuthState>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<QrLoginSession>> BeginQrLoginAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await _api.PostApiFormAsync(
                "/api/login/qrcode/unikey",
                [new KeyValuePair<string, string>("type", "3")],
                ct);
            var root = doc.RootElement;
            if (!root.TryGetProperty("unikey", out var keyEl))
            {
                return Result<QrLoginSession>.Failure($"{MusicSourceErrorCodes.Network}: 获取二维码 key 失败");
            }

            var key = keyEl.GetString()!;
            return Result<QrLoginSession>.Success(new QrLoginSession
            {
                Key = key,
                QrUrl = $"https://music.163.com/login?codekey={key}"
            });
        }
        catch (Exception ex)
        {
            return Result<QrLoginSession>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<QrLoginPollResult>> PollQrLoginAsync(string sessionKey, CancellationToken ct = default)
    {
        try
        {
            using var doc = await _api.PostApiFormAsync(
                "/api/login/qrcode/client/login",
                [
                    new KeyValuePair<string, string>("key", sessionKey),
                    new KeyValuePair<string, string>("type", "3")
                ],
                ct);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            var status = code switch
            {
                800 => QrLoginStatus.Expired,
                801 => QrLoginStatus.Waiting,
                802 => QrLoginStatus.Scanned,
                803 => QrLoginStatus.Confirmed,
                _ => QrLoginStatus.Unknown
            };

            return Result<QrLoginPollResult>.Success(new QrLoginPollResult
            {
                Status = status,
                Message = msg,
                Nickname = root.TryGetProperty("nickname", out var n) ? n.GetString() : null,
                AvatarUrl = root.TryGetProperty("avatarUrl", out var a) ? a.GetString() : null
            });
        }
        catch (Exception ex)
        {
            return Result<QrLoginPollResult>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public Task<Result> SignInWithCookieAsync(string cookieHeader, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return Task.FromResult(Result.Failure("Cookie 为空"));
        }

        _api.ApplyCookieHeader(cookieHeader);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SignOutAsync(CancellationToken ct = default)
    {
        _api.ClearSession();
        return Task.FromResult(Result.Success());
    }

    public async Task<Result<IReadOnlyList<MusicTrackRef>>> GetLikedTracksAsync(CancellationToken ct = default)
    {
        var auth = await GetAuthStateAsync(ct);
        if (!auth.IsSuccess || auth.Value is null || !auth.Value.IsAuthenticated || auth.Value.UserId is null)
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure($"{MusicSourceErrorCodes.NotLoggedIn}: 请先登录网易云");
        }

        try
        {
            using var likeDoc = await _api.GetApiAsync($"/api/song/like/get?uid={auth.Value.UserId}", ct);
            var ids = likeDoc.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetInt64()).ToArray();
            if (ids.Length == 0)
            {
                return Result<IReadOnlyList<MusicTrackRef>>.Success([]);
            }

            var tracks = new List<MusicTrackRef>(ids.Length);
            const int batch = 200;
            for (var i = 0; i < ids.Length; i += batch)
            {
                ct.ThrowIfCancellationRequested();
                var slice = ids.Skip(i).Take(batch).ToArray();
                var detail = await FetchSongDetailsAsync(slice, ct);
                if (!detail.IsSuccess)
                {
                    return Result<IReadOnlyList<MusicTrackRef>>.Failure(detail.Error!, detail.Exception);
                }

                tracks.AddRange(detail.Value!);
            }

            return Result<IReadOnlyList<MusicTrackRef>>.Success(tracks);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<IReadOnlyList<MusicTrackRef>>> GetDailyRecommendAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await _api.WeapiAsync(
                "/weapi/v2/discovery/recommend/songs",
                new { limit = 30, offset = 0, total = true },
                ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 200)
            {
                return Result<IReadOnlyList<MusicTrackRef>>.Failure(
                    $"{MusicSourceErrorCodes.NotLoggedIn}: 日推失败 code={code.GetInt32()}");
            }

            var songs = root.TryGetProperty("recommend", out var rec) && rec.ValueKind == JsonValueKind.Array
                ? rec
                : root.GetProperty("data").GetProperty("dailySongs");

            return Result<IReadOnlyList<MusicTrackRef>>.Success(ParseTracks(songs));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetHistoryRecommendDatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await _api.WeapiAsync("/weapi/discovery/recommend/songs/history/recent", new { }, ct);
            var dates = doc.RootElement.GetProperty("data").GetProperty("dates")
                .EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList();
            return Result<IReadOnlyList<string>>.Success(dates);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<IReadOnlyList<MusicTrackRef>>> GetHistoryRecommendAsync(string date, CancellationToken ct = default)
    {
        try
        {
            using var doc = await _api.WeapiAsync(
                "/weapi/discovery/recommend/songs/history/detail",
                new { date },
                ct);
            var songs = doc.RootElement.GetProperty("data").GetProperty("songs");
            return Result<IReadOnlyList<MusicTrackRef>>.Success(ParseTracks(songs));
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure($"{MusicSourceErrorCodes.Network}: {ex.Message}", ex);
        }
    }

    public async Task<Result<ResolvedAudio>> ResolveAudioAsync(
        MusicTrackRef track,
        bool preferTemporary = true,
        CancellationToken ct = default)
    {
        if (!long.TryParse(track.ExternalId, out var songId))
        {
            return Result<ResolvedAudio>.Failure("无效的网易云 songId");
        }

        try
        {
            using var doc = await _api.WeapiAsync(
                "/weapi/song/enhance/player/url/v1",
                new
                {
                    ids = JsonSerializer.Serialize(new[] { songId }),
                    level = _options.AudioLevel,
                    encodeType = "mp3"
                },
                ct);

            var data0 = doc.RootElement.GetProperty("data")[0];
            var code = data0.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            if (code != 200 || !data0.TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
            {
                var fee = data0.TryGetProperty("fee", out var f) ? f.GetInt32() : (int?)null;
                var err = fee == 8
                    ? $"{MusicSourceErrorCodes.VipExpired}: 无法获取播放地址（可能需 VIP）"
                    : $"{MusicSourceErrorCodes.NoCopyright}: 无法获取播放地址 code={code}";
                return Result<ResolvedAudio>.Failure(err);
            }

            var url = urlEl.GetString()!;
            var type = data0.TryGetProperty("type", out var t) ? t.GetString() : "mp3";
            var br = data0.TryGetProperty("br", out var b) ? b.GetInt32() : (int?)null;
            var dir = ResolveTempDir();
            var ext = string.IsNullOrWhiteSpace(type) ? "mp3" : type.ToLowerInvariant();
            var path = Path.Combine(dir, $"{songId}_{_options.AudioLevel}_{Guid.NewGuid():N}.{ext}");
            await _api.DownloadAsync(url, path, ct);

            return Result<ResolvedAudio>.Success(new ResolvedAudio
            {
                FilePath = path,
                IsTemporary = preferTemporary,
                Format = ext,
                Bitrate = br
            });
        }
        catch (Exception ex)
        {
            return Result<ResolvedAudio>.Failure($"{MusicSourceErrorCodes.DownloadFailed}: {ex.Message}", ex);
        }
    }

    private async Task<Result<IReadOnlyList<MusicTrackRef>>> FetchSongDetailsAsync(long[] ids, CancellationToken ct)
    {
        var c = JsonSerializer.Serialize(ids.Select(id => new { id = id.ToString(CultureInfo.InvariantCulture) }));
        var idsJson = JsonSerializer.Serialize(ids);
        using var doc = await _api.WeapiAsync("/weapi/v3/song/detail", new { c, ids = idsJson }, ct);
        if (!doc.RootElement.TryGetProperty("songs", out var songs))
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure("song/detail 无 songs");
        }

        return Result<IReadOnlyList<MusicTrackRef>>.Success(ParseTracks(songs));
    }

    private static List<MusicTrackRef> ParseTracks(JsonElement songs)
    {
        var list = new List<MusicTrackRef>();
        foreach (var s in songs.EnumerateArray())
        {
            var artists = new List<string>();
            if (s.TryGetProperty("ar", out var ar))
            {
                foreach (var a in ar.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && n.GetString() is { } name)
                    {
                        artists.Add(name);
                    }
                }
            }
            else if (s.TryGetProperty("artists", out var artistsEl))
            {
                foreach (var a in artistsEl.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && n.GetString() is { } name)
                    {
                        artists.Add(name);
                    }
                }
            }

            string? album = null;
            if (s.TryGetProperty("al", out var al) && al.TryGetProperty("name", out var aln))
            {
                album = aln.GetString();
            }
            else if (s.TryGetProperty("album", out var albumEl) && albumEl.TryGetProperty("name", out var an))
            {
                album = an.GetString();
            }

            var reason = s.TryGetProperty("reason", out var r) ? r.GetString()
                : s.TryGetProperty("recommendReason", out var rr) ? rr.GetString() : null;
            var alg = s.TryGetProperty("alg", out var ag) ? ag.GetString() : null;
            var dt = s.TryGetProperty("dt", out var d) ? d.GetInt32()
                : s.TryGetProperty("duration", out var du) ? du.GetInt32() : (int?)null;
            var fee = s.TryGetProperty("fee", out var f) ? f.GetInt32() : (int?)null;

            list.Add(new MusicTrackRef
            {
                SourceId = MusicSourceIds.Netease,
                ExternalId = s.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
                Title = s.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
                Artists = artists,
                Album = album,
                DurationMs = dt,
                Fee = fee,
                RecommendReason = reason,
                Algorithm = alg
            });
        }

        return list;
    }

    private string ResolveTempDir()
    {
        if (!string.IsNullOrWhiteSpace(_options.TempDownloadDirectory))
        {
            return _options.TempDownloadDirectory;
        }

        return Path.Combine(Path.GetTempPath(), "FindMyFavouriteMusic", "netease");
    }
}
