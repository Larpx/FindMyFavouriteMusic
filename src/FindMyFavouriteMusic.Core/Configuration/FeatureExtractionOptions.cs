namespace FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// 特征提取配置
/// </summary>
public class FeatureExtractionOptions
{
    public const string SectionName = "FeatureExtraction";

    /// <summary>MFCC 系数数量</summary>
    public int MfccCoefficientCount { get; set; } = 13;

    /// <summary>梅尔滤波器组数量</summary>
    public int MelFilterBankSize { get; set; } = 26;

    /// <summary>帧长（秒）</summary>
    public double FrameDurationSeconds { get; set; } = 0.025;

    /// <summary>帧移（秒）</summary>
    public double HopDurationSeconds { get; set; } = 0.010;

    /// <summary>目标采样率</summary>
    public int TargetSampleRate { get; set; } = 16000;
}
