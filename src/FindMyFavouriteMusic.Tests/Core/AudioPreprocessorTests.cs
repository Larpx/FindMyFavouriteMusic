using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// <see cref="AudioPreprocessor"/> 单声道合并与重采样测试。
/// </summary>
public class AudioPreprocessorTests
{
    [Fact]
    public void Process_Stereo_ConvertsToMonoAverage()
    {
        var preprocessor = new AudioPreprocessor(16000, channels: 2, targetSampleRate: 16000);
        // interleaved: L0=1, R0=3 → mono 2; L1=2, R1=4 → mono 3
        var result = preprocessor.Process([1f, 3f, 2f, 4f]);
        result.Should().Equal(2f, 3f);
    }

    [Fact]
    public void Process_MonoSameRate_ReturnsSameReferenceLength()
    {
        var preprocessor = new AudioPreprocessor(16000, channels: 1, targetSampleRate: 16000);
        var samples = new float[] { 0.1f, 0.2f, 0.3f };
        preprocessor.Process(samples).Should().Equal(samples);
    }

    [Fact]
    public void Process_Downsample_HalvesLengthApproximately()
    {
        var preprocessor = new AudioPreprocessor(32000, channels: 1, targetSampleRate: 16000);
        var samples = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();
        var result = preprocessor.Process(samples);
        result.Length.Should().Be(50);
    }
}
