namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 用户配置文件路径（AppData），避免写入安装目录权限问题。
/// </summary>
public static class UserSettingsPaths
{
    public const string FileName = "usersettings.json";
    public const string AppFolderName = "FindMyFavouriteMusic";

    /// <summary>
    /// %AppData%/FindMyFavouriteMusic/usersettings.json
    /// </summary>
    public static string GetUserSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    /// <summary>应用目录下的旧版配置路径（用于一次性迁移）。</summary>
    public static string GetLegacySettingsFilePath() =>
        Path.Combine(AppContext.BaseDirectory, FileName);
}
