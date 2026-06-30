using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Base;
using NWaves.FeatureExtractors.Multi;
using NWaves.FeatureExtractors.Options;

namespace FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 基于 NWaves 的声学特征提取器
/// 输出维度: MFCC(26) + 频谱质心(2) + 色度(24) = 52 维
/// </summary>
public class AcousticFeatureExtractor : IAcousticFeatureExtractor
{
    private readonly FeatureExtractionOptions _options;
    private readonly IFeatureAggregator _aggregator;
    private readonly ILogger<AcousticFeatureExtractor> _logger;

    /// <summary>
    /// MFCC: 13 均值 + 13 方差 = 26
    /// 频谱质心: 1 均值 + 1 方差 = 2
    /// 色度: 12 均值 + 12 方差 = 24
    /// 总计 = 52
    /// </summary>
    private const int TotalDimension = 52;

    public AcousticFeatureExtractor(
        IOptions<FeatureExtractionOptions> options,
        IFeatureAggregator aggregator,
        ILogger<AcousticFeatureExtractor> logger)
    {
        _options = options.Value;
        _aggregator = aggregator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public int FeatureDimension => TotalDimension;

    /// <inheritdoc/>
    public Result<float[]> Extract(float[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length == 0)
        {
            return Result<float[]>.Failure("音频采样数据为空");
        }

        try
        {
            var mfccCount = _options.MfccCoefficientCount;

            // 1. 提取 MFCC
            var mfccExtractor = new MfccExtractor(new MfccOptions
            {
                SamplingRate = sampleRate,
                FeatureCount = mfccCount,
                FilterBankSize = _options.MelFilterBankSize,
                FftSize = 512,
                FrameDuration = _options.FrameDurationSeconds,
                HopDuration = _options.HopDurationSeconds
            });
            var mfccFrames = mfccExtractor.ComputeFrom(samples).ToArray();

            // 2. 提取频谱特征（含频谱质心）
            var spectralExtractor = new SpectralFeaturesExtractor(new MultiFeatureOptions
            {
                SamplingRate = sampleRate,
                FeatureList = "centroid",
                FftSize = 512,
                FrameDuration = _options.FrameDurationSeconds,
                HopDuration = _options.HopDurationSeconds
            });
            var spectralFrames = spectralExtractor.ComputeFrom(samples).ToArray();

            // 3. 提取色度特征
            var chromaExtractor = new ChromaExtractor(new ChromaOptions
            {
                SamplingRate = sampleRate,
                FftSize = 512,
                FrameDuration = _options.FrameDurationSeconds,
                HopDuration = _options.HopDurationSeconds
            });
            var chromaFrames = chromaExtractor.ComputeFrom(samples).ToArray();

            // 4. 聚合各特征
            var mfccVector = _aggregator.Aggregate(mfccFrames);
            var spectralVector = _aggregator.Aggregate(spectralFrames);
            var chromaVector = _aggregator.Aggregate(chromaFrames);

            // 5. 拼接为最终向量
            var result = new float[TotalDimension];
            var offset = 0;

            Array.Copy(mfccVector, 0, result, offset, Math.Min(mfccVector.Length, mfccCount * 2));
            offset += mfccCount * 2;

            Array.Copy(spectralVector, 0, result, offset, Math.Min(spectralVector.Length, 2));
            offset += 2;

            Array.Copy(chromaVector, 0, result, offset, Math.Min(chromaVector.Length, 24));

            return Result<float[]>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "声学特征提取失败");
            return Result<float[]>.Failure(ex);
        }
    }
}
