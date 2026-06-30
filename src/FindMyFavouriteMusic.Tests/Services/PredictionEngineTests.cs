using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// PredictionEngine 单元测试，验证加权评分计算与降级策略。
/// </summary>
/// <remarks>
/// 通过 mock ISimilarityCalculator 控制相似度返回值，
/// mock IDeepFeatureExtractor 控制 IsModelLoaded 开关，
/// 从而隔离测试 PredictionEngine 的评分公式与降级逻辑。
/// </remarks>
public class PredictionEngineTests
{
    private readonly Mock<ISimilarityCalculator> _similarityMock;
    private readonly Mock<IDeepFeatureExtractor> _deepExtractorMock;
    private readonly PredictionEngine _engine;

    public PredictionEngineTests()
    {
        _similarityMock = new Mock<ISimilarityCalculator>();
        _deepExtractorMock = new Mock<IDeepFeatureExtractor>();
        // 权重：声学 0.4 / 深度 0.6 / 仅声学 1.0
        var options = Options.Create(new PredictionOptions
        {
            AcousticWeight = 0.4,
            DeepWeight = 0.6,
            AcousticOnlyWeight = 1.0
        });
        _engine = new PredictionEngine(
            _similarityMock.Object,
            _deepExtractorMock.Object,
            options,
            Mock.Of<ILogger<PredictionEngine>>());
    }

    /// <summary>
    /// 深度模型未加载时，应进入仅声学模式，Score = AcousticOnlyWeight * MapToScore(acousticSim)。
    /// </summary>
    [Fact]
    public void Predict_AcousticOnly_ReturnsAcousticMode()
    {
        // Arrange: 模型未加载，声学相似度 = 1.0（映射为 100 分）
        _deepExtractorMock.SetupGet(d => d.IsModelLoaded).Returns(false);
        _similarityMock.Setup(s => s.Calculate(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(Result<double>.Success(1.0));

        // Act
        var result = _engine.Predict(
            new float[] { 1f, 0f }, null,
            new float[] { 1f, 0f }, null);

        // Assert: Score = 1.0 * 100 = 100，模式为仅声学
        result.IsSuccess.Should().BeTrue();
        result.Value!.Mode.Should().Be(PredictionMode.AcousticOnly);
        result.Value.Score.Should().BeApproximately(100.0, 0.001);
        result.Value.AcousticScore.Should().BeApproximately(100.0, 0.001);
        result.Value.DeepScore.Should().BeNull();
    }

    /// <summary>
    /// 模型已加载且提供深度向量时，应使用加权评分：Score = 0.4*acoustic + 0.6*deep。
    /// </summary>
    [Fact]
    public void Predict_AcousticAndDeep_ReturnsWeightedScore()
    {
        // Arrange: 模型已加载；声学相似度 = 1.0（100 分），深度相似度 = 0.0（50 分）
        _deepExtractorMock.SetupGet(d => d.IsModelLoaded).Returns(true);
        _similarityMock.SetupSequence(s => s.Calculate(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(Result<double>.Success(1.0))  // 第一次调用：声学相似度
            .Returns(Result<double>.Success(0.0)); // 第二次调用：深度相似度

        // Act
        var result = _engine.Predict(
            new float[] { 1f }, new float[] { 1f },
            new float[] { 1f }, new float[] { 1f });

        // Assert: Score = 0.4*100 + 0.6*50 = 40 + 30 = 70
        result.IsSuccess.Should().BeTrue();
        result.Value!.Mode.Should().Be(PredictionMode.AcousticAndDeep);
        result.Value.Score.Should().BeApproximately(70.0, 0.001);
        result.Value.AcousticScore.Should().BeApproximately(100.0, 0.001);
        result.Value.DeepScore.Should().BeApproximately(50.0, 0.001);
    }

    /// <summary>
    /// 深度相似度计算失败时，应降级为仅声学模式并返回声学评分。
    /// </summary>
    [Fact]
    public void Predict_DeepSimilarityFails_DegradesToAcousticOnly()
    {
        // Arrange: 模型已加载；声学相似度成功，深度相似度失败
        _deepExtractorMock.SetupGet(d => d.IsModelLoaded).Returns(true);
        _similarityMock.SetupSequence(s => s.Calculate(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(Result<double>.Success(1.0))                    // 声学相似度
            .Returns(Result<double>.Failure("深度计算失败"));         // 深度相似度

        // Act
        var result = _engine.Predict(
            new float[] { 1f }, new float[] { 1f },
            new float[] { 1f }, new float[] { 1f });

        // Assert: 降级为仅声学模式，Score = 100
        result.IsSuccess.Should().BeTrue();
        result.Value!.Mode.Should().Be(PredictionMode.AcousticOnly);
        result.Value.Score.Should().BeApproximately(100.0, 0.001);
        result.Value.AcousticScore.Should().BeApproximately(100.0, 0.001);
        result.Value.DeepScore.Should().BeNull();
    }

    /// <summary>
    /// 声学相似度计算失败时，应直接返回失败结果。
    /// </summary>
    [Fact]
    public void Predict_SimilarityFails_ReturnsFailure()
    {
        // Arrange: 声学相似度计算失败
        _deepExtractorMock.SetupGet(d => d.IsModelLoaded).Returns(false);
        _similarityMock.Setup(s => s.Calculate(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(Result<double>.Failure("声学计算失败"));

        // Act
        var result = _engine.Predict(
            new float[] { 1f }, null,
            new float[] { 1f }, null);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// 验证 MapToScore 映射公式：(sim + 1) / 2 * 100，
    /// 相似度 1 → 100，相似度 0 → 50，相似度 -1 → 0。
    /// 通过 Predict 间接验证（AcousticOnlyWeight = 1.0，Score 即为映射值）。
    /// </summary>
    /// <param name="similarity">余弦相似度输入</param>
    /// <param name="expectedScore">期望评分</param>
    [Theory]
    [InlineData(1.0, 100.0)]
    [InlineData(0.0, 50.0)]
    [InlineData(-1.0, 0.0)]
    public void Predict_MapToScore_MapsCorrectly(double similarity, double expectedScore)
    {
        // Arrange: 模型未加载，仅使用声学相似度
        _deepExtractorMock.SetupGet(d => d.IsModelLoaded).Returns(false);
        _similarityMock.Setup(s => s.Calculate(It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(Result<double>.Success(similarity));

        // Act
        var result = _engine.Predict(
            new float[] { 1f }, null,
            new float[] { 1f }, null);

        // Assert: AcousticOnlyWeight = 1.0，Score = 1.0 * MapToScore(similarity)
        result.IsSuccess.Should().BeTrue();
        result.Value!.Score.Should().BeApproximately(expectedScore, 0.001);
        result.Value.AcousticScore.Should().BeApproximately(expectedScore, 0.001);
    }
}
