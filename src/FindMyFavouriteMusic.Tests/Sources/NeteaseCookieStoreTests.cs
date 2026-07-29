using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class NeteaseCookieStoreTests : IDisposable
{
    private readonly string _cookiePath = Path.Combine(
        Path.GetTempPath(),
        $"netease_cookie_{Guid.NewGuid():N}.dat");

    [Fact]
    public void Save_Load_Clear_RoundTrip()
    {
        var options = Options.Create(new NeteaseOptions { CookieFilePath = _cookiePath });
        var store = new NeteaseCookieStore(options, Mock.Of<ILogger<NeteaseCookieStore>>());

        store.CookieHeader.Should().BeNull();
        store.Save("MUSIC_U=abc; __csrf=xyz");
        store.CookieHeader.Should().Be("MUSIC_U=abc; __csrf=xyz");
        File.Exists(_cookiePath).Should().BeTrue();

        var reloaded = new NeteaseCookieStore(options, Mock.Of<ILogger<NeteaseCookieStore>>());
        reloaded.CookieHeader.Should().Be("MUSIC_U=abc; __csrf=xyz");

        reloaded.Clear();
        reloaded.CookieHeader.Should().BeNull();
        File.Exists(_cookiePath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (File.Exists(_cookiePath))
        {
            File.Delete(_cookiePath);
        }
    }
}
