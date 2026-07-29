using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

/// <summary>
/// 网易云 Web API 跑通冒烟测试（真实网络）。
/// </summary>
/// <remarks>
/// <para>默认跳过，避免 CI/日常单测挂起。启用方式任选其一：</para>
/// <list type="bullet">
/// <item><c>NETEASE_API_SMOKE=1</c></item>
/// <item>环境变量 <c>NETEASE_COOKIE</c>（Cookie 头）</item>
/// </list>
/// <para>登录优先级：<c>NETEASE_COOKIE</c> → 已保存 Cookie 文件 → 二维码轮询（最长约 2 分钟，需手机确认）。</para>
/// <para>验证流程：登录 → 用户信息/VIP → 红心列表 → 日推列表 → 随机各下载 1 首并删除临时文件。</para>
/// </remarks>
public class NeteaseApiSmokeTests : IDisposable
{
    private const string SmokeEnv = "NETEASE_API_SMOKE";
    private const string CookieEnv = "NETEASE_COOKIE";

    private readonly ITestOutputHelper _output;
    private readonly string _cookiePath;
    private readonly string _tempDir;
    private readonly NeteaseMusicSourcePlugin _plugin;
    private readonly HttpClient _http;

    public NeteaseApiSmokeTests(ITestOutputHelper output)
    {
        _output = output;
        _cookiePath = Path.Combine(Path.GetTempPath(), $"netease_smoke_{Guid.NewGuid():N}.dat");
        _tempDir = Path.Combine(Path.GetTempPath(), "FindMyFavouriteMusic", $"smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new NeteaseOptions
        {
            CookieFilePath = _cookiePath,
            TempDownloadDirectory = _tempDir,
            AudioLevel = "exhigh"
        });
        var cookieStore = new NeteaseCookieStore(options, NullLogger<NeteaseCookieStore>.Instance);
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        var api = new NeteaseApiClient(_http, cookieStore, NullLogger<NeteaseApiClient>.Instance);
        _plugin = new NeteaseMusicSourcePlugin(api, options, NullLogger<NeteaseMusicSourcePlugin>.Instance);
    }

    [Fact]
    public void ApiContract_HasTimestampVersionId_AndKnownEndpoints()
    {
        NeteaseApiContract.VersionId.Should().MatchRegex(@"^\d{14}$");
        NeteaseApiContract.Endpoints.DailyRecommend.Should().Contain("/weapi/v2/");
        NeteaseApiContract.Endpoints.PlayerUrlV1.Should().Contain("player/url/v1");
        _plugin.ApiVersionId.Should().Be(NeteaseApiContract.VersionId);
        _output.WriteLine($"Netease API VersionId={NeteaseApiContract.VersionId} ({NeteaseApiContract.VersionNote})");
    }

    [Fact]
    public async Task Smoke_QrLogin_UserVip_Liked_Daily_RandomDownload()
    {
        if (!IsSmokeEnabled())
        {
            _output.WriteLine(
                $"跳过网易云跑通测试：未设置 {SmokeEnv}=1 且未提供 {CookieEnv}。" +
                "本地验证示例：$env:NETEASE_API_SMOKE='1'; 或提供 NETEASE_COOKIE。");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;

        _output.WriteLine($"API VersionId={_plugin.ApiVersionId}");

        await EnsureLoggedInAsync(ct);

        var auth = await _plugin.GetAuthStateAsync(ct);
        auth.IsSuccess.Should().BeTrue(auth.Error);
        auth.Value!.IsAuthenticated.Should().BeTrue("应已登录");
        auth.Value.UserId.Should().NotBeNullOrWhiteSpace();
        _output.WriteLine(
            $"用户: {auth.Value.DisplayName} uid={auth.Value.UserId} " +
            $"VIP={auth.Value.IsVip} Lv={auth.Value.VipLevel} 到期={auth.Value.VipExpireAt}");

        var liked = await _plugin.GetLikedTracksAsync(ct);
        liked.IsSuccess.Should().BeTrue(liked.Error);
        liked.Value.Should().NotBeNull();
        _output.WriteLine($"红心数量: {liked.Value!.Count}");
        liked.Value.Count.Should().BeGreaterThan(0, "红心列表为空，无法验证下载");

        var daily = await _plugin.GetDailyRecommendAsync(ct);
        daily.IsSuccess.Should().BeTrue(daily.Error);
        daily.Value.Should().NotBeNull();
        _output.WriteLine($"日推数量: {daily.Value!.Count}");
        daily.Value.Count.Should().BeGreaterThan(0, "日推为空，无法验证下载");

        var rng = Random.Shared;
        var likedPick = liked.Value[rng.Next(liked.Value.Count)];
        var dailyPick = daily.Value[rng.Next(daily.Value.Count)];
        _output.WriteLine($"随机红心: {likedPick.Title} / {string.Join(',', likedPick.Artists)} ({likedPick.ExternalId})");
        _output.WriteLine($"随机日推: {dailyPick.Title} / {string.Join(',', dailyPick.Artists)} ({dailyPick.ExternalId})");

        await DownloadAndAssertAsync(likedPick, "liked", ct);
        await DownloadAndAssertAsync(dailyPick, "daily", ct);
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        var cookie = Environment.GetEnvironmentVariable(CookieEnv);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            var signIn = await _plugin.SignInWithCookieAsync(cookie, ct);
            signIn.IsSuccess.Should().BeTrue(signIn.Error);
            _output.WriteLine("已使用 NETEASE_COOKIE 登录");
            return;
        }

        // 尝试默认 AppData Cookie（GUI 扫码后留下的会话）
        var appDataCookie = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FindMyFavouriteMusic",
            "netease.cookie.dat");
        if (File.Exists(appDataCookie))
        {
            var appOptions = Options.Create(new NeteaseOptions { CookieFilePath = appDataCookie });
            var store = new NeteaseCookieStore(appOptions, NullLogger<NeteaseCookieStore>.Instance);
            if (!string.IsNullOrWhiteSpace(store.CookieHeader))
            {
                var signIn = await _plugin.SignInWithCookieAsync(store.CookieHeader, ct);
                signIn.IsSuccess.Should().BeTrue(signIn.Error);
                var state = await _plugin.GetAuthStateAsync(ct);
                if (state is { IsSuccess: true, Value.IsAuthenticated: true })
                {
                    _output.WriteLine("已使用 AppData 已存 Cookie 登录");
                    return;
                }
            }
        }

        _output.WriteLine("无可用 Cookie，开始二维码登录流程…");
        var session = await _plugin.BeginQrLoginAsync(ct);
        session.IsSuccess.Should().BeTrue(session.Error);
        _output.WriteLine($"请用网易云 App 扫码并确认：{session.Value!.QrUrl}");
        _output.WriteLine($"unikey={session.Value.Key}");

        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var poll = await _plugin.PollQrLoginAsync(session.Value.Key, ct);
            poll.IsSuccess.Should().BeTrue(poll.Error);
            _output.WriteLine($"QR 状态: {poll.Value!.Status} {poll.Value.Message}");

            if (poll.Value.Status == QrLoginStatus.Confirmed)
            {
                return;
            }

            if (poll.Value.Status == QrLoginStatus.Expired)
            {
                throw new InvalidOperationException("二维码已过期，请重新运行冒烟测试");
            }

            await Task.Delay(1500, ct);
        }

        throw new InvalidOperationException("二维码登录超时（2 分钟内未确认）");
    }

    private async Task DownloadAndAssertAsync(MusicTrackRef track, string label, CancellationToken ct)
    {
        var resolve = await _plugin.ResolveAudioAsync(track, preferTemporary: true, ct);
        resolve.IsSuccess.Should().BeTrue($"{label} 下载失败: {resolve.Error}");
        var path = resolve.Value!.FilePath;
        await using (resolve.Value)
        {
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(1024, $"{label} 文件过小");
            _output.WriteLine($"{label} 下载成功: {path} ({new FileInfo(path).Length} bytes, {resolve.Value.Format}/{resolve.Value.Bitrate})");
        }

        File.Exists(path).Should().BeFalse($"{label} 临时文件应在用后删除");
    }

    private static bool IsSmokeEnabled()
    {
        var flag = Environment.GetEnvironmentVariable(SmokeEnv);
        if (string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CookieEnv));
    }

    public void Dispose()
    {
        _http.Dispose();
        try
        {
            if (File.Exists(_cookiePath))
            {
                File.Delete(_cookiePath);
            }

            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
