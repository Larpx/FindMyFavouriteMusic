namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Helpers;

/// <summary>
/// 测试用极简 WAV 文件生成器（PCM 16-bit mono 8kHz）。
/// </summary>
internal static class WavTestFile
{
    /// <summary>
    /// 写入可被 TagLib / NAudio 打开的短静音 WAV。
    /// </summary>
    public static string CreateSilentWav(string? directory = null, int sampleCount = 8000)
    {
        directory ??= Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"test_{Guid.NewGuid():N}.wav");

        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = (short)(channels * (bitsPerSample / 8));

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        // data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);

        return path;
    }
}
