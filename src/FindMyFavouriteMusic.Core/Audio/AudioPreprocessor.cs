namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 音频预处理器：将解码后的原始采样转换为特征提取所需的标准格式。
/// </summary>
/// <remarks>
/// 处理步骤：
/// <para>1. 多声道合并为单声道（取各声道平均值）；</para>
/// <para>2. 重采样到目标采样率（默认 16kHz，VGGish 等模型的标准输入）。</para>
/// <para>这两步是音频特征提取前的标准预处理，确保不同来源的音频具有统一的时频表示。</para>
/// </remarks>
public class AudioPreprocessor
{
    private readonly int _sourceSampleRate;
    private readonly int _channels;
    private readonly int _targetSampleRate;

    public AudioPreprocessor(int sourceSampleRate, int channels, int targetSampleRate)
    {
        _sourceSampleRate = sourceSampleRate;
        _channels = channels;
        _targetSampleRate = targetSampleRate;
    }

    /// <summary>
    /// 处理音频采样：立体声转单声道 + 降采样。
    /// </summary>
    /// <param name="samples"> interleaved 多声道采样数据（如立体声为 [L0,R0,L1,R1,...]）</param>
    /// <returns>单声道、目标采样率的采样数组</returns>
    public float[] Process(float[] samples)
    {
        // 步骤 1：多声道转单声道（若输入已是单声道则跳过）
        var mono = _channels > 1 ? ConvertToMono(samples, _channels) : samples;

        // 步骤 2：重采样到目标采样率（若源采样率已等于目标则跳过）
        var resampled = _sourceSampleRate != _targetSampleRate
            ? Resample(mono, _sourceSampleRate, _targetSampleRate)
            : mono;

        return resampled;
    }

    /// <summary>
    /// 立体声/多声道转单声道：对每个采样帧取各声道的平均值。
    /// <para>公式：mono[i] = (L[i] + R[i]) / 2（以立体声为例）</para>
    /// <para>这样可以保留两个声道的信息，避免单侧声道丢失内容。</para>
    /// </summary>
    private static float[] ConvertToMono(float[] samples, int channels)
    {
        var frameCount = samples.Length / channels;
        var mono = new float[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            var sum = 0f;
            for (var ch = 0; ch < channels; ch++)
            {
                sum += samples[i * channels + ch];
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>
    /// 简单线性插值重采样。
    /// </summary>
    /// <remarks>
    /// 对每个目标采样点，找到源序列中相邻的两个点，按比例线性插值：
    /// <para>y[i] = x[i/ratio] × (1 - frac) + x[i/ratio + 1] × frac</para>
    /// <para>其中 ratio = sourceRate / targetRate，frac 为小数部分。</para>
    /// <para>优势：实现简单、计算快速；劣势：高频成分可能产生混叠失真。</para>
    /// <para>对于音乐品味预测场景，线性插值的精度已足够。</para>
    /// </remarks>
    private static float[] Resample(float[] samples, int sourceRate, int targetRate)
    {
        var ratio = (double)sourceRate / targetRate;
        var outputLength = (int)(samples.Length / ratio);
        var output = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            var sourceIndex = i * ratio;
            var index1 = (int)sourceIndex;
            var index2 = Math.Min(index1 + 1, samples.Length - 1);
            var fraction = sourceIndex - index1;
            output[i] = (float)(samples[index1] * (1 - fraction) + samples[index2] * fraction);
        }

        return output;
    }
}
