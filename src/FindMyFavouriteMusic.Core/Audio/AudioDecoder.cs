using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Enums;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 基于 NAudio 的音频解码器。
/// <para>解码流程：</para>
/// <para>1. 通过文件扩展名识别格式；</para>
/// <para>2. 选择对应的 Reader：WAV 使用 WaveFileReader，MP3 使用 Mp3FileReader，FLAC/M4A 使用 MediaFoundationReader（仅 Windows）；</para>
/// <para>3. 读取 PCM 字节并按位深度转换为 32 位浮点采样；</para>
/// <para>4. 通过 <see cref="AudioPreprocessor"/> 完成多声道合并与重采样。</para>
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
    public bool SupportsFormat(AudioFormat format) => format switch
    {
        AudioFormat.Wav => true,
        AudioFormat.Mp3 => true,
        // FLAC/M4A 依赖 Windows Media Foundation，仅在 Windows 平台可用
        AudioFormat.Flac => OperatingSystem.IsWindows(),
        AudioFormat.M4a => OperatingSystem.IsWindows(),
        _ => false
    };

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
            // 解码属于 CPU 密集型操作，使用 Task.Run 切换到线程池避免阻塞调用线程
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

    /// <summary>
    /// 实际的解码逻辑：创建 Reader → 读取 PCM → 转浮点 → 预处理。
    /// </summary>
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

    /// <summary>
    /// 根据音频格式创建对应的 NAudio Reader。
    /// FLAC/M4A 在 Windows 上通过 MediaFoundationReader 解码（依赖系统解码器）。
    /// </summary>
    private static WaveStream CreateReader(string filePath, AudioFormat format) => format switch
    {
        AudioFormat.Wav => new WaveFileReader(filePath),
        AudioFormat.Mp3 => new Mp3FileReader(filePath),
        AudioFormat.Flac when OperatingSystem.IsWindows() => new MediaFoundationReader(filePath),
        AudioFormat.M4a when OperatingSystem.IsWindows() => new MediaFoundationReader(filePath),
        _ => throw new NotSupportedException($"不支持的格式: {format}")
    };

    /// <summary>
    /// 将 PCM 字节缓冲区转换为浮点采样数组，根据位深度选择对应转换方法。
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

    /// <summary>16 位 PCM 转 float，归一化到 [-1, 1] 区间（除以 32768）。</summary>
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

    /// <summary>24 位 PCM 转 float，归一化到 [-1, 1] 区间（除以 8388608）。</summary>
    private static float[] Convert24BitToFloat(byte[] buffer, int bytesRead)
    {
        var sampleCount = bytesRead / 3;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var byteIndex = i * 3;
            var sample = buffer[byteIndex] | (buffer[byteIndex + 1] << 8) | (buffer[byteIndex + 2] << 16);
            // 24 位有符号整数符号位扩展
            if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
            samples[i] = sample / 8388608f;
        }
        return samples;
    }

    /// <summary>32 位 PCM 转 float，归一化到 [-1, 1] 区间（除以 2147483648）。</summary>
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
