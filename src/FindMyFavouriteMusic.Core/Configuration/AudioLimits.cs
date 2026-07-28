namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;

/// <summary>
/// 音频处理硬限制（扫描 / 预测 / 拖拽统一）。
/// </summary>
public static class AudioLimits
{
    /// <summary>单文件最大字节数：200MB。</summary>
    public const long MaxFileSizeBytes = 200L * 1024 * 1024;

    /// <summary>用于错误提示的可读大小文案。</summary>
    public const string MaxFileSizeDisplay = "200MB";
}
