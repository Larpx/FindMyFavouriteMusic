namespace FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 扫描配置
/// </summary>
public class ScanOptions
{
    public const string SectionName = "Scan";

    /// <summary>支持的音频文件扩展名</summary>
    public List<string> SupportedExtensions { get; set; } = [".mp3", ".wav", ".flac", ".m4a"];

    /// <summary>最大并发处理数</summary>
    public int MaxConcurrentProcessing { get; set; } = 2;
}
