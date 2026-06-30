using FindMyFavouriteMusic.Models.Enums;
using FindMyFavouriteMusic.Models.Results;

namespace FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 音频信息
/// </summary>
public record AudioInfo(TimeSpan Duration, int SampleRate, int Channels, AudioFormat Format);

/// <summary>
/// 音频解码接口，隔离平台差异
/// </summary>
public interface IAudioDecoder
{
    /// <summary>检查是否支持指定格式</summary>
    bool SupportsFormat(AudioFormat format);

    /// <summary>解码为单声道目标采样率的 float 数组</summary>
    Task<Result<float[]>> DecodeAsync(string filePath, CancellationToken ct = default);

    /// <summary>获取音频元信息</summary>
    Task<Result<AudioInfo>> GetAudioInfoAsync(string filePath);
}
