using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// 数据库 schema 迁移测试（PRAGMA user_version）。
/// </summary>
public class DatabaseMigrationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public DatabaseMigrationTests()
    {
        var dbName = $"migrate_test_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    [Fact]
    public async Task StartAsync_UpgradesV0Schema_ToUserVersion1()
    {
        // Arrange: 模拟旧库（仅基线列，user_version=0）
        await using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE Songs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT UNIQUE NOT NULL,
                    Title TEXT,
                    Artist TEXT,
                    IsLiked INTEGER DEFAULT 0,
                    AcousticVector BLOB,
                    DeepVector BLOB
                );
                CREATE TABLE UserProfile (
                    Id INTEGER PRIMARY KEY,
                    AcousticMeanVector BLOB,
                    DeepMeanVector BLOB,
                    LastUpdated DATETIME
                );
                PRAGMA user_version = 0;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var initializer = new DatabaseInitializer(
            Options.Create(new DatabaseOptions { ConnectionString = _connectionString }),
            Mock.Of<ILogger<DatabaseInitializer>>());

        // Act
        await initializer.StartAsync(CancellationToken.None);

        // Assert
        await using var versionCmd = _keepAlive.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version";
        Convert.ToInt32(await versionCmd.ExecuteScalarAsync()).Should().Be(DatabaseInitializer.CurrentSchemaVersion);

        var columns = await GetColumnsAsync("Songs");
        columns.Should().Contain("FileMd5");
        columns.Should().Contain("DeepModelType");
        columns.Should().Contain("Album");
        columns.Should().Contain("Lyrics");

        var profileColumns = await GetColumnsAsync("UserProfile");
        profileColumns.Should().Contain("AcousticSampleCount");
        profileColumns.Should().Contain("DeepSampleCount");
    }

    private async Task<HashSet<string>> GetColumnsAsync(string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            set.Add(reader.GetString(1));
        }

        return set;
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
    }
}
