namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 扫描配置
/// </summary>
public class ScanOptions
{
    public const string SectionName = "Scan";

    /// <summary>支持的音频文件扩展名</summary>
    public List<string> SupportedExtensions { get; set; } = [".mp3", ".wav", ".flac", ".ogg", ".m4a"];

    /// <summary>最大并发处理数</summary>
    public int MaxConcurrentProcessing { get; set; } = 2;

    /// <summary>
    /// 上次扫描的音乐库目录路径，用于程序启动时自动重新扫描。
    /// </summary>
    /// <remarks>
    /// 该字段由 UserSettingsService 持久化到 usersettings.json，
    /// 首次扫描时为 null，用户完成首次扫描后自动保存，后续启动时自动读取并触发后台扫描。
    /// </remarks>
    public string? LastScanDirectory { get; set; }
}
