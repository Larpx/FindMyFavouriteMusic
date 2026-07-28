namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Netease;

public sealed class NeteaseOptions
{
    public const string SectionName = "Netease";

    /// <summary>Cookie 持久化路径（默认 AppData）。</summary>
    public string? CookieFilePath { get; set; }

    /// <summary>临时下载目录。</summary>
    public string? TempDownloadDirectory { get; set; }

    /// <summary>播放音质 level：standard / exhigh / lossless。</summary>
    public string AudioLevel { get; set; } = "exhigh";
}
