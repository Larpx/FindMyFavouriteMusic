using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease.Crypto;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class WeapiCryptoTests
{
    [Fact]
    public void Encrypt_ReturnsNonEmptyParamsAndEncSecKey()
    {
        var (p, enc) = WeapiCrypto.Encrypt("""{"limit":30,"offset":0,"total":true}""");
        p.Should().NotBeNullOrWhiteSpace();
        enc.Should().NotBeNullOrWhiteSpace();
        enc.Length.Should().Be(256);
    }
}
