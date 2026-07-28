using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 数据库初始化器：建表 + <c>PRAGMA user_version</c> 迁移。
/// </summary>
public class DatabaseInitializer : IHostedService
{
    /// <summary>当前目标 schema 版本（B1）。</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly DatabaseOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IOptions<DatabaseOptions> options,
        ILogger<DatabaseInitializer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("正在初始化数据库: {ConnectionString}", _options.ConnectionString);

        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);

            await EnsureBaseTablesAsync(connection, ct);
            await MigrateAsync(connection, ct);

            _logger.LogInformation("数据库初始化完成（user_version={Version}）", CurrentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库初始化失败");
            throw;
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task EnsureBaseTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        // 基线表（v0）：旧库可能已有；新库先建基线再走迁移补列
        const string createSongsTable = """
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

        const string createUserProfileTable = """
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
    }

    private async Task MigrateAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version";
        var versionObj = await versionCmd.ExecuteScalarAsync(ct);
        var version = Convert.ToInt32(versionObj);

        if (version < 1)
        {
            _logger.LogInformation("执行数据库迁移: v{From} → v1", version);
            await MigrateToV1Async(connection, ct);
            await SetUserVersionAsync(connection, 1, ct);
        }
    }

    private static async Task MigrateToV1Async(SqliteConnection connection, CancellationToken ct)
    {
        // Songs：指纹 / 特征契约 / 元数据镜像列
        string[] songColumns =
        [
            "FileMd5 TEXT",
            "FileSize INTEGER",
            "DurationMs INTEGER",
            "Format TEXT",
            "AcousticDim INTEGER",
            "DeepModelType TEXT",
            "DeepDim INTEGER",
            "FeatureExtractedAt DATETIME",
            "Album TEXT",
            "AlbumArtist TEXT",
            "Genre TEXT",
            "Year INTEGER",
            "Track TEXT",
            "Disc TEXT",
            "Comment TEXT",
            "Lyrics TEXT"
        ];

        foreach (var columnDef in songColumns)
        {
            await AddColumnIfMissingAsync(connection, "Songs", columnDef, ct);
        }

        // UserProfile：声学/深度样本数
        await AddColumnIfMissingAsync(connection, "UserProfile", "AcousticSampleCount INTEGER DEFAULT 0", ct);
        await AddColumnIfMissingAsync(connection, "UserProfile", "DeepSampleCount INTEGER DEFAULT 0", ct);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, string table, string columnDef, CancellationToken ct)
    {
        var columnName = columnDef.Split(' ', 2)[0];
        if (await ColumnExistsAsync(connection, table, columnName, ct))
        {
            return;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {columnDef}";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection, string table, string columnName, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // table_info: cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task SetUserVersionAsync(SqliteConnection connection, int version, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        // PRAGMA user_version 不支持参数绑定，版本号来自常量
        cmd.CommandText = $"PRAGMA user_version = {version}";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
