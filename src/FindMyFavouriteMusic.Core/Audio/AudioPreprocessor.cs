namespace FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 音频预处理器：重采样、单声道转换、归一化
/// </summary>
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
    /// 处理音频采样：立体声转单声道 + 降采样 + 归一化
    /// </summary>
    public float[] Process(float[] samples)
    {
        var mono = _channels > 1 ? ConvertToMono(samples, _channels) : samples;
        var resampled = _sourceSampleRate != _targetSampleRate
            ? Resample(mono, _sourceSampleRate, _targetSampleRate)
            : mono;
        return resampled;
    }

    /// <summary>
    /// 立体声/多声道转单声道（取平均）
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
    /// 简单线性插值重采样
    /// </summary>
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
