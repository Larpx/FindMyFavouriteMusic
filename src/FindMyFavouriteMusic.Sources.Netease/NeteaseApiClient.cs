using System.Net;
using System.Text.Json;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease.Crypto;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;

/// <summary>网易云 Web API 客户端（weapi + 明文 /api）。</summary>
public sealed class NeteaseApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly NeteaseCookieStore _cookies;
    private readonly ILogger<NeteaseApiClient> _logger;
    private readonly CookieContainer _jar = new();

    public NeteaseApiClient(
        HttpClient http,
        NeteaseCookieStore cookies,
        ILogger<NeteaseApiClient> logger)
    {
        _http = http;
        _cookies = cookies;
        _logger = logger;
        ApplyStoredCookies();
    }

    public void ApplyCookieHeader(string cookieHeader)
    {
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var name = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            _jar.Add(new Uri("https://music.163.com/"), new Cookie(name, value, "/", ".music.163.com"));
        }

        _cookies.Save(BuildCookieHeader());
    }

    public void ClearSession()
    {
        _cookies.Clear();
        // CookieContainer 无 Clear API：用过期时间清空已知 Cookie
        foreach (Cookie c in _jar.GetCookies(new Uri("https://music.163.com/")))
        {
            c.Expired = true;
        }
    }

    public string? GetCookieHeader() => BuildCookieHeader();

    public async Task<JsonDocument> GetApiAsync(string pathAndQuery, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://music.163.com" + pathAndQuery);
        AttachCookies(req);
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(text);
    }

    public async Task<JsonDocument> PostApiFormAsync(string path, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com" + path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        AttachCookies(req);
        using var res = await _http.SendAsync(req, ct);
        MergeSetCookie(res);
        var text = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(text);
    }

    public async Task<JsonDocument> WeapiAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var (p, enc) = WeapiCrypto.Encrypt(json);
        var csrf = GetCookieValue("__csrf") ?? "";
        var url = path.Contains('?', StringComparison.Ordinal)
            ? $"https://music.163.com{path}&csrf_token={csrf}"
            : $"https://music.163.com{path}?csrf_token={csrf}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["params"] = p,
                ["encSecKey"] = enc
            })
        };
        AttachCookies(req);
        using var res = await _http.SendAsync(req, ct);
        MergeSetCookie(res);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("weapi HTTP {Status}: {Path}", (int)res.StatusCode, path);
        }

        return JsonDocument.Parse(text);
    }

    public async Task DownloadAsync(string url, string destPath, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://music.163.com/");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using var fs = File.Create(destPath);
        await res.Content.CopyToAsync(fs, ct);
    }

    private void ApplyStoredCookies()
    {
        var header = _cookies.CookieHeader;
        if (!string.IsNullOrWhiteSpace(header))
        {
            ApplyCookieHeader(header);
        }
    }

    private void AttachCookies(HttpRequestMessage req)
    {
        var header = BuildCookieHeader();
        if (!string.IsNullOrEmpty(header))
        {
            req.Headers.TryAddWithoutValidation("Cookie", header);
        }

        req.Headers.Referrer = new Uri("https://music.163.com/");
    }

    private void MergeSetCookie(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var raw in values)
        {
            var first = raw.Split(';')[0];
            var idx = first.IndexOf('=');
            if (idx <= 0) continue;
            var name = first[..idx].Trim();
            var value = first[(idx + 1)..].Trim();
            _jar.Add(new Uri("https://music.163.com/"), new Cookie(name, value, "/", ".music.163.com"));
        }

        var header = BuildCookieHeader();
        if (!string.IsNullOrEmpty(header))
        {
            _cookies.Save(header);
        }
    }

    private string BuildCookieHeader()
    {
        var cookies = _jar.GetCookies(new Uri("https://music.163.com/")).Cast<Cookie>();
        return string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
    }

    private string? GetCookieValue(string name) =>
        _jar.GetCookies(new Uri("https://music.163.com/"))[name]?.Value;
}
