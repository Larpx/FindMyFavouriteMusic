using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Enums;
using FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 基于 NAudio 的音频解码器，支持 WAV 和 MP3
/// </summary>
public class AudioDecoder : IAudioDecoder
{
    private readonly FeatureExtractionOptions _options;
    private readonly ILogger<AudioDecoder> _logger;

    public AudioDecoder(
        IOptions<FeatureExtractionOptions> options,
        ILogger<AudioDecoder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsFormat(AudioFormat format) => format is AudioFormat.Wav or AudioFormat.Mp3;

    /// <inheritdoc/>
    public async Task<Result<float[]>> DecodeAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return Result<float[]>.Failure($"文件不存在: {filePath}");
        }

        var format = AudioFormatDetector.DetectFromExtension(filePath);
        if (!SupportsFormat(format))
        {
            return Result<float[]>.Failure($"不支持的音频格式: {format}");
        }

        try
        {
            var samples = await Task.Run(() => DecodeInternal(filePath, format), ct);
            return Result<float[]>.Success(samples);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "音频解码失败: {FilePath}", filePath);
            return Result<float[]>.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public Task<Result<AudioInfo>> GetAudioInfoAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult(Result<AudioInfo>.Failure($"文件不存在: {filePath}"));
        }

        var format = AudioFormatDetector.DetectFromExtension(filePath);
        if (!SupportsFormat(format))
        {
            return Task.FromResult(Result<AudioInfo>.Failure($"不支持的音频格式: {format}"));
        }

        try
        {
            using var reader = CreateReader(filePath, format);
            var info = new AudioInfo(
                reader.TotalTime,
                reader.WaveFormat.SampleRate,
                reader.WaveFormat.Channels,
                format);
            return Task.FromResult(Result<AudioInfo>.Success(info));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取音频信息失败: {FilePath}", filePath);
            return Task.FromResult(Result<AudioInfo>.Failure(ex));
        }
    }

    private float[] DecodeInternal(string filePath, AudioFormat format)
    {
        using var reader = CreateReader(filePath, format);
        var preprocessor = new AudioPreprocessor(
            reader.WaveFormat.SampleRate,
            reader.WaveFormat.Channels,
            _options.TargetSampleRate);

        // 读取所有采样数据
        var bytesToRead = (int)reader.Length;
        var buffer = new byte[bytesToRead];
        var bytesRead = reader.Read(buffer, 0, bytesToRead);

        // 转换为 32 位浮点
        var samples = ConvertToFloatSamples(buffer, bytesRead, reader.WaveFormat.BitsPerSample);

        // 预处理：重采样 + 单声道
        return preprocessor.Process(samples);
    }

    private static NAudio.Wave.WaveStream CreateReader(string filePath, AudioFormat format) => format switch
    {
        AudioFormat.Wav => new NAudio.Wave.WaveFileReader(filePath),
        AudioFormat.Mp3 => new NAudio.Wave.Mp3FileReader(filePath),
        _ => throw new NotSupportedException($"不支持的格式: {format}")
    };

    /// <summary>
    /// 将 PCM 字节缓冲区转换为浮点采样数组
    /// </summary>
    private static float[] ConvertToFloatSamples(byte[] buffer, int bytesRead, int bitsPerSample)
    {
        return bitsPerSample switch
        {
            16 => Convert16BitToFloat(buffer, bytesRead),
            24 => Convert24BitToFloat(buffer, bytesRead),
            32 => Convert32BitToFloat(buffer, bytesRead),
            _ => throw new NotSupportedException($"不支持的位深度: {bitsPerSample}")
        };
    }

    private static float[] Convert16BitToFloat(byte[] buffer, int bytesRead)
    {
        var sampleCount = bytesRead / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * 2);
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    private static float[] Convert24BitToFloat(byte[] buffer, int bytesRead)
    {
        var sampleCount = bytesRead / 3;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var byteIndex = i * 3;
            var sample = buffer[byteIndex] | (buffer[byteIndex + 1] << 8) | (buffer[byteIndex + 2] << 16);
            if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
            samples[i] = sample / 8388608f;
        }
        return samples;
    }

    private static float[] Convert32BitToFloat(byte[] buffer, int bytesRead)
    {
        var sampleCount = bytesRead / 4;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt32(buffer, i * 4);
            samples[i] = sample / 2147483648f;
        }
        return samples;
    }
}
