using System.Security.Cryptography;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 文件内容 MD5 指纹（用于扫描缓存：未变则跳过重算）。
/// </summary>
public static class FileContentHasher
{
    /// <summary>
    /// 计算文件内容 MD5，返回小写十六进制字符串。
    /// </summary>
    public static async Task<string> ComputeMd5HexAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 128, useAsync: true);
        var hash = await MD5.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
