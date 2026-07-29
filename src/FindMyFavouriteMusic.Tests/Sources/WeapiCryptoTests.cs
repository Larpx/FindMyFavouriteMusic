using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease.Crypto;
using System.Text.RegularExpressions;

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
        Regex.IsMatch(enc, "^[0-9a-f]{256}$").Should().BeTrue();
    }

    [Fact]
    public void Encrypt_UsesRandomSecret_SoParamsDifferAcrossCalls()
    {
        const string json = """{"csrf_token":""}""";
        var (p1, enc1) = WeapiCrypto.Encrypt(json);
        var (p2, enc2) = WeapiCrypto.Encrypt(json);

        p1.Should().NotBe(p2);
        enc1.Should().NotBe(enc2);
        enc1.Length.Should().Be(256);
        enc2.Length.Should().Be(256);
    }

    [Fact]
    public void Encrypt_EncSecKey_IsExactly256LowerHex_OverManyRandomKeys()
    {
        // 覆盖随机 AES secretKey → RSA，确保无需（也禁止）截断高位即可稳定得到 256 hex
        for (var i = 0; i < 64; i++)
        {
            var (_, enc) = WeapiCrypto.Encrypt($$"""{"i":{{i}},"csrf_token":""}""");
            enc.Length.Should().Be(256);
            Regex.IsMatch(enc, "^[0-9a-f]{256}$").Should().BeTrue();
        }
    }
}
