using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Multi;
using NWaves.FeatureExtractors.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 基于 NWaves 的声学特征提取器。
/// </summary>
/// <remarks>
/// 输出维度构成：MFCC(13 均值 + 13 方差) + 频谱质心(1 均值 + 1 方差) + 色度(12 均值 + 12 方差) = 52 维。
/// <para>处理流程：</para>
/// <para>1. 使用 NWaves 的 MfccExtractor 提取帧级 MFCC；</para>
/// <para>2. 使用 SpectralFeaturesExtractor 提取帧级频谱质心；</para>
/// <para>3. 使用 ChromaExtractor 提取帧级色度特征；</para>
/// <para>4. 通过 IFeatureAggregator 将帧级特征聚合为（均值 + 方差）向量；</para>
/// <para>5. 拼接为 52 维最终向量；</para>
/// <para>6. 若启用归一化，使用 Z-Score 处理。</para>
/// </remarks>
public class AcousticFeatureExtractor : IAcousticFeatureExtractor
{
    /// <summary>
    /// 最终特征向量维度：
    /// MFCC: 13 均值 + 13 方差 = 26
    /// 频谱质心: 1 均值 + 1 方差 = 2
    /// 色度: 12 均值 + 12 方差 = 24
    /// 合计 = 52
    /// </summary>
    private const int TotalDimension = 52;

    private readonly FeatureExtractionOptions _options;
    private readonly IFeatureAggregator _aggregator;
    private readonly ILogger<AcousticFeatureExtractor> _logger;

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

            // 1. 提取 MFCC（梅尔频率倒谱系数）：模拟人耳对频率的非线性感知
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

            // 2. 提取频谱质心：衡量声音"亮度"的中心频率
            var spectralExtractor = new SpectralFeaturesExtractor(new MultiFeatureOptions
            {
                SamplingRate = sampleRate,
                FeatureList = "centroid",
                FftSize = 512,
                FrameDuration = _options.FrameDurationSeconds,
                HopDuration = _options.HopDurationSeconds
            });
            var spectralFrames = spectralExtractor.ComputeFrom(samples).ToArray();

            // 3. 提取色度特征：12 个音高类的能量分布，反映音调/和声内容
            var chromaExtractor = new ChromaExtractor(new ChromaOptions
            {
                SamplingRate = sampleRate,
                FftSize = 512,
                FrameDuration = _options.FrameDurationSeconds,
                HopDuration = _options.HopDurationSeconds
            });
            var chromaFrames = chromaExtractor.ComputeFrom(samples).ToArray();

            // 4. 聚合各特征（每帧特征 → 单个均值+方差向量）
            var mfccVector = _aggregator.Aggregate(mfccFrames);
            var spectralVector = _aggregator.Aggregate(spectralFrames);
            var chromaVector = _aggregator.Aggregate(chromaFrames);

            // 5. 拼接为最终 52 维向量
            var result = new float[TotalDimension];
            var offset = 0;

            Array.Copy(mfccVector, 0, result, offset, Math.Min(mfccVector.Length, mfccCount * 2));
            offset += mfccCount * 2;

            Array.Copy(spectralVector, 0, result, offset, Math.Min(spectralVector.Length, 2));
            offset += 2;

            Array.Copy(chromaVector, 0, result, offset, Math.Min(chromaVector.Length, 24));

            // 6. 可选：Z-Score 归一化（默认关闭，见 FeatureExtractionOptions.EnableNormalization）
            if (_options.EnableNormalization)
            {
                result = FeatureNormalizer.Normalize(result);
            }

            return Result<float[]>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "声学特征提取失败");
            return Result<float[]>.Failure(ex);
        }
    }
}
