using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease.Crypto;

/// <summary>网易云 weapi params/encSecKey 加密（与 Web asrsea 兼容）。</summary>
public static class WeapiCrypto
{
    private const string PresetKey = "0CoJUm6Qyw8W8jud";
    private const string Iv = "0102030405060708";
    private const string PubKey = "010001";
    // 网易云 Web asrsea 固定 RSA modulus（256 hex）
    private const string Modulus =
        "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b72515" +
        "2b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbd" +
        "a92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cf" +
        "e4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";

    public static (string Params, string EncSecKey) Encrypt(string plainJson)
    {
        var secretKey = CreateSecretKey(16);
        var params1 = AesEncrypt(plainJson, PresetKey);
        var params2 = AesEncrypt(params1, secretKey);
        var encSecKey = RsaEncrypt(secretKey, PubKey, Modulus);
        return (params2, encSecKey);
    }

    private static string CreateSecretKey(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(chars[bytes[i] % chars.Length]);
        }

        return sb.ToString();
    }

    private static string AesEncrypt(string text, string key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(key);
        aes.IV = Encoding.UTF8.GetBytes(Iv);
        using var encryptor = aes.CreateEncryptor();
        var input = Encoding.UTF8.GetBytes(text);
        var encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>网易云自定义 RSA：明文反转后按无填充大数模幂。</summary>
    private static string RsaEncrypt(string text, string pubKeyHex, string modulusHex)
    {
        var reversed = new string(text.Reverse().ToArray());
        var buffer = Encoding.UTF8.GetBytes(reversed);
        var hex = Convert.ToHexString(buffer).ToLowerInvariant();
        var biText = BigInteger.Parse("00" + hex, System.Globalization.NumberStyles.AllowHexSpecifier);
        var biEx = BigInteger.Parse("00" + pubKeyHex, System.Globalization.NumberStyles.AllowHexSpecifier);
        var biMod = BigInteger.Parse(modulusHex, System.Globalization.NumberStyles.AllowHexSpecifier);
        var biRet = BigInteger.ModPow(biText, biEx, biMod);
        var result = biRet.ToString("x");
        if (result.Length > 256)
        {
            result = result[^256..];
        }

        return result.PadLeft(256, '0');
    }
}
