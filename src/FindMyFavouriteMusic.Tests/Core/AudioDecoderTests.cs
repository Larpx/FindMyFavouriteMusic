using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// AudioDecoder 硬限制相关测试。
/// </summary>
public class AudioDecoderTests
{
    /// <summary>
    /// 超过 200MB 的文件应在解码前被拒绝。
    /// </summary>
    [Fact]
    public async Task DecodeAsync_FileExceeds200Mb_ReturnsFailure()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"oversized_{Guid.NewGuid():N}.mp3");
        try
        {
            // 稀疏文件：设置长度超过硬限，无需实际写入 200MB 数据
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(AudioLimits.MaxFileSizeBytes + 1);
            }

            var decoder = new AudioDecoder(
                Options.Create(new FeatureExtractionOptions()),
                Mock.Of<ILogger<AudioDecoder>>());

            var result = await decoder.DecodeAsync(tempPath);

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain(AudioLimits.MaxFileSizeDisplay);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
