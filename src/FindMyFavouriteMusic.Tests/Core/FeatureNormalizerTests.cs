using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// <see cref="FeatureNormalizer"/> Z-Score 归一化单元测试。
/// </summary>
public class FeatureNormalizerTests
{
    [Fact]
    public void Normalize_VariedValues_HasNearZeroMeanAndUnitStd()
    {
        var input = new float[] { 1f, 2f, 3f, 4f, 5f };
        var result = FeatureNormalizer.Normalize(input);

        result.Average().Should().BeApproximately(0f, 1e-5f);
        Math.Sqrt(result.Average(f => f * f)).Should().BeApproximately(1.0, 1e-5);
    }

    [Fact]
    public void Normalize_ConstantVector_ReturnsOriginal()
    {
        var input = new float[] { 2f, 2f, 2f };
        FeatureNormalizer.Normalize(input).Should().Equal(input);
    }

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        FeatureNormalizer.Normalize([]).Should().BeEmpty();
    }

    [Fact]
    public void Normalize_Null_Throws()
    {
        var act = () => FeatureNormalizer.Normalize(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
