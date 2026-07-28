using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Helpers;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// <see cref="FileContentHasher"/> 单元测试。
/// </summary>
public class FileContentHasherTests
{
    [Fact]
    public async Task ComputeMd5HexAsync_SameContent_SameHash()
    {
        var path = WavTestFile.CreateSilentWav();
        try
        {
            var a = await FileContentHasher.ComputeMd5HexAsync(path);
            var b = await FileContentHasher.ComputeMd5HexAsync(path);
            a.Should().Be(b);
            a.Should().HaveLength(32);
            a.Should().MatchRegex("^[0-9a-f]{32}$");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeMd5HexAsync_DifferentContent_DifferentHash()
    {
        var path1 = WavTestFile.CreateSilentWav(sampleCount: 1000);
        var path2 = WavTestFile.CreateSilentWav(sampleCount: 2000);
        try
        {
            var a = await FileContentHasher.ComputeMd5HexAsync(path1);
            var b = await FileContentHasher.ComputeMd5HexAsync(path2);
            a.Should().NotBe(b);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }
}
