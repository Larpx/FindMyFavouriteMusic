using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

public class CosineSimilarityCalculatorTests
{
    private readonly CosineSimilarityCalculator _calculator = new();

    [Fact]
    public void Calculate_SameVector_ReturnsOne()
    {
        var vector = new float[] { 1, 0, 0 };
        var result = _calculator.Calculate(vector, vector);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.0, result.Value, 5);
    }

    [Fact]
    public void Calculate_OrthogonalVectors_ReturnsZero()
    {
        var vectorA = new float[] { 1, 0, 0 };
        var vectorB = new float[] { 0, 1, 0 };
        var result = _calculator.Calculate(vectorA, vectorB);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.0, result.Value, 5);
    }

    [Fact]
    public void Calculate_OppositeVectors_ReturnsMinusOne()
    {
        var vectorA = new float[] { 1, 0, 0 };
        var vectorB = new float[] { -1, 0, 0 };
        var result = _calculator.Calculate(vectorA, vectorB);

        Assert.True(result.IsSuccess);
        Assert.Equal(-1.0, result.Value, 5);
    }

    [Fact]
    public void Calculate_ZeroVector_ReturnsZero()
    {
        var vectorA = new float[] { 0, 0, 0 };
        var vectorB = new float[] { 1, 2, 3 };
        var result = _calculator.Calculate(vectorA, vectorB);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.0, result.Value, 5);
    }

    [Fact]
    public void Calculate_DimensionMismatch_ReturnsFailure()
    {
        var vectorA = new float[] { 1, 2 };
        var vectorB = new float[] { 1, 2, 3 };
        var result = _calculator.Calculate(vectorA, vectorB);

        Assert.False(result.IsSuccess);
        Assert.Contains("维度不匹配", result.Error);
    }

    [Fact]
    public void Calculate_EmptyVector_ReturnsFailure()
    {
        var vectorA = Array.Empty<float>();
        var vectorB = Array.Empty<float>();
        var result = _calculator.Calculate(vectorA, vectorB);

        Assert.False(result.IsSuccess);
    }
}
