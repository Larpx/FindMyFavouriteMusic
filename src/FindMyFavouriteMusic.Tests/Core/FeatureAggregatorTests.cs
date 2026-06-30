using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

public class FeatureAggregatorTests
{
    private readonly FeatureAggregator _aggregator = new();

    [Fact]
    public void Aggregate_SingleFrame_ReturnsMeanAndZeroVariance()
    {
        var frames = new float[][] { new float[] { 1, 2, 3 } };
        var result = _aggregator.Aggregate(frames);

        // 均值部分
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
        Assert.Equal(3, result[2]);
        // 方差部分
        Assert.Equal(0, result[3]);
        Assert.Equal(0, result[4]);
        Assert.Equal(0, result[5]);
    }

    [Fact]
    public void Aggregate_MultipleFrames_ComputesCorrectMeanAndVariance()
    {
        var frames = new float[][]
        {
            [2f, 4f],
            [4f, 6f]
        };
        var result = _aggregator.Aggregate(frames);

        // 均值: (2+4)/2=3, (4+6)/2=5
        Assert.Equal(3, result[0]);
        Assert.Equal(5, result[1]);

        // 方差: ((2-3)^2 + (4-3)^2)/2=1, ((4-5)^2 + (6-5)^2)/2=1
        Assert.Equal(1, result[2]);
        Assert.Equal(1, result[3]);
    }

    [Fact]
    public void Aggregate_EmptyFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => _aggregator.Aggregate([]));
    }
}
