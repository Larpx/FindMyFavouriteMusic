using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 数据库初始化器，应用启动时自动建表
/// </summary>
public class DatabaseInitializer : IHostedService
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseInitializer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("正在初始化数据库: {ConnectionString}", _options.ConnectionString);

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);

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

            var createUserProfileTable = """
                CREATE TABLE IF NOT EXISTS UserProfile (
                    Id INTEGER PRIMARY KEY,
                    AcousticMeanVector BLOB,
                    DeepMeanVector BLOB,
                    LastUpdated DATETIME
                )
                """;

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
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
