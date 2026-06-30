namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// 特征提取配置。
/// 所有参数均可通过 appsettings.json 的 "FeatureExtraction" 节点覆盖。
/// </summary>
public class FeatureExtractionOptions
{
    public const string SectionName = "FeatureExtraction";

    /// <summary>MFCC 系数数量（典型值 13~20）</summary>
    public int MfccCoefficientCount { get; set; } = 13;

    /// <summary>梅尔滤波器组数量（典型值为 MFCC 系数数的 2 倍）</summary>
    public int MelFilterBankSize { get; set; } = 26;

    /// <summary>帧长（秒），通常为 25ms，对应语音短时平稳假设</summary>
    public double FrameDurationSeconds { get; set; } = 0.025;

    /// <summary>帧移（秒），通常为 10ms，对应 60% 重叠</summary>
    public double HopDurationSeconds { get; set; } = 0.010;

    /// <summary>目标采样率（Hz），VGGish 与多数声学模型使用 16kHz</summary>
    public int TargetSampleRate { get; set; } = 16000;

    /// <summary>
    /// 是否对最终聚合的特征向量进行 Z-Score 归一化。
    /// <para>默认关闭：对拼接后的异构特征做整体 Z-Score 会抹平各子特征间的尺度差异，</para>
    /// <para>可能损害余弦相似度的判别力。如需实验可设为 true。</para>
    /// </summary>
    public bool EnableNormalization { get; set; } = false;
}
