using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 数据库初始化器，实现 IHostedService 在应用启动时自动建表。
/// </summary>
/// <remarks>
/// 职责：在应用启动阶段确保 Songs 与 UserProfile 两张表已存在，避免运行期查询失败。
/// 建表使用 CREATE TABLE IF NOT EXISTS，具备幂等性——多次重启不会报错或重置数据。
/// 失败时抛出异常以阻止应用以损坏的数据库状态启动（fail-fast 原则）。
/// </remarks>
public class DatabaseInitializer : IHostedService
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">数据库配置（含连接字符串）</param>
    /// <param name="logger">日志记录器</param>
    public DatabaseInitializer(
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseInitializer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 应用启动时执行建表操作。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// 失败时抛出异常，阻止应用启动——避免在缺失表的数据库上运行导致后续业务异常。
    /// </remarks>
    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("正在初始化数据库: {ConnectionString}", _options.ConnectionString);

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);

            // Songs 表设计：
            // - Id：自增主键；
            // - FilePath：UNIQUE 约束，避免同一文件被重复扫描入库；
            // - IsLiked：INTEGER(0/1) 表示是否喜欢，默认 0；
            // - AcousticVector / DeepVector：以 BLOB 存储序列化后的特征向量。
            var createSongsTable = """
                CREATE TABLE IF NOT EXISTS Songs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT UNIQUE NOT NULL,
                    Title TEXT,
                    Artist TEXT,
                    IsLiked INTEGER DEFAULT 0,
                    AcousticVector BLOB,
                    DeepVector BLOB
                )
                """;

            // UserProfile 表设计：
            // - Id：主键（固定为 1），不使用 AUTOINCREMENT，因为这是单行表；
            // - 声学/深度均值向量以 BLOB 存储；
            // - LastUpdated 记录画像最近一次更新时间。
            var createUserProfileTable = """
                CREATE TABLE IF NOT EXISTS UserProfile (
                    Id INTEGER PRIMARY KEY,
                    AcousticMeanVector BLOB,
                    DeepMeanVector BLOB,
                    LastUpdated DATETIME
                )
                """;

            // 使用原生 SqliteCommand 执行 DDL（无需 Dapper）
            await using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = createSongsTable;
            await cmd1.ExecuteNonQueryAsync(ct);

            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = createUserProfileTable;
            await cmd2.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库初始化失败");
            // 重新抛出：阻止应用以损坏的数据库状态启动（fail-fast）
            throw;
        }
    }

    /// <summary>
    /// 应用停止时无需任何操作（连接由 using 自动释放）。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
