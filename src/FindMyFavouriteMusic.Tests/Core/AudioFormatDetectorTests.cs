using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Enums;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// <see cref="AudioFormatDetector"/> 扩展名映射测试。
/// </summary>
public class AudioFormatDetectorTests
{
    [Theory]
    [InlineData("a.mp3", AudioFormat.Mp3)]
    [InlineData("a.WAV", AudioFormat.Wav)]
    [InlineData("a.wave", AudioFormat.Wav)]
    [InlineData("a.flac", AudioFormat.Flac)]
    [InlineData("a.ogg", AudioFormat.Ogg)]
    [InlineData("a.m4a", AudioFormat.M4a)]
    [InlineData("a.txt", AudioFormat.Unknown)]
    public void DetectFromExtension_MapsKnownFormats(string path, AudioFormat expected)
    {
        AudioFormatDetector.DetectFromExtension(path).Should().Be(expected);
    }

    [Fact]
    public void IsSupportedExtension_TrueForMp3_FalseForTxt()
    {
        AudioFormatDetector.IsSupportedExtension("x.mp3").Should().BeTrue();
        AudioFormatDetector.IsSupportedExtension("x.txt").Should().BeFalse();
    }
}
