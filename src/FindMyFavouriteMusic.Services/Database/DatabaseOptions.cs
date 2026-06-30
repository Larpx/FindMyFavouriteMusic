namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 数据库配置
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>SQLite 连接字符串</summary>
    public string ConnectionString { get; set; } = "Data Source=findmyfavouritemusic.db";
}
