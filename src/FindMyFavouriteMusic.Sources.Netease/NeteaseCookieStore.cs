using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;

/// <summary>Cookie 持久化（Windows 下 DPAPI 保护）。</summary>
public sealed class NeteaseCookieStore
{
    private readonly NeteaseOptions _options;
    private readonly ILogger<NeteaseCookieStore> _logger;
    private readonly object _gate = new();
    private string? _cookieHeader;

    public NeteaseCookieStore(IOptions<NeteaseOptions> options, ILogger<NeteaseCookieStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _cookieHeader = TryLoad();
    }

    public string? CookieHeader
    {
        get { lock (_gate) return _cookieHeader; }
    }

    public void Save(string cookieHeader)
    {
        lock (_gate)
        {
            _cookieHeader = cookieHeader;
            var path = ResolvePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var plain = Encoding.UTF8.GetBytes(cookieHeader);
            byte[] payload = OperatingSystem.IsWindows()
                ? ProtectWindows(plain)
                : plain;
            File.WriteAllBytes(path, payload);
            _logger.LogInformation("网易云 Cookie 已保存（已脱敏路径）");
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cookieHeader = null;
            var path = ResolvePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private string? TryLoad()
    {
        try
        {
            var path = ResolvePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            var plain = OperatingSystem.IsWindows() ? UnprotectWindows(bytes) : bytes;
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取网易云 Cookie 失败");
            return null;
        }
    }

    private string ResolvePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CookieFilePath))
        {
            return _options.CookieFilePath;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FindMyFavouriteMusic");
        return Path.Combine(dir, "netease.cookie.dat");
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plain) =>
        ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] payload) =>
        ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
