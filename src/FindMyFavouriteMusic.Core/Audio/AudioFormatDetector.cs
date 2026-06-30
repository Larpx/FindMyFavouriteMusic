using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Enums;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;

/// <summary>
/// 音频格式检测器
/// </summary>
public static class AudioFormatDetector
{
    private static readonly Dictionary<string, AudioFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".wav"] = AudioFormat.Wav,
        [".wave"] = AudioFormat.Wav,
        [".mp3"] = AudioFormat.Mp3,
        [".flac"] = AudioFormat.Flac,
        [".m4a"] = AudioFormat.M4a
    };

    /// <summary>根据文件扩展名判断音频格式</summary>
    public static AudioFormat DetectFromExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return ExtensionMap.TryGetValue(extension, out var format) ? format : AudioFormat.Unknown;
    }

    /// <summary>检查文件扩展名是否为支持的音频格式</summary>
    public static bool IsSupportedExtension(string filePath)
    {
        return DetectFromExtension(filePath) != AudioFormat.Unknown;
    }
}
