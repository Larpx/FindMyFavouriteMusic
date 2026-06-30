using Dapper;
using FindMyFavouriteMusic.Models.Entities;
using FindMyFavouriteMusic.Models.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 用户画像仓储实现
/// </summary>
public class ProfileRepository
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<ProfileRepository> _logger;

    public ProfileRepository(
        IOptions<DatabaseOptions> options,
        ILogger<ProfileRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>获取用户画像</summary>
    public async Task<Result<UserProfile?>> GetAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM UserProfile WHERE Id = 1";
            var row = await connection.QueryFirstOrDefaultAsync<ProfileRow>(sql);

            return Result<UserProfile?>.Success(row?.ToUserProfile());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户画像失败");
            return Result<UserProfile?>.Failure(ex);
        }
    }

    /// <summary>保存用户画像（upsert）</summary>
    public async Task<Result> SaveAsync(UserProfile profile)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = """
                INSERT INTO UserProfile (Id, AcousticMeanVector, DeepMeanVector, LastUpdated)
                VALUES (1, @AcousticMeanVectorBlob, @DeepMeanVectorBlob, @LastUpdated)
                ON CONFLICT(Id) DO UPDATE SET
                    AcousticMeanVector = @AcousticMeanVectorBlob,
                    DeepMeanVector = @DeepMeanVectorBlob,
                    LastUpdated = @LastUpdated
                """;

            await connection.ExecuteAsync(sql, new
            {
                profile.AcousticMeanVectorBlob,
                profile.DeepMeanVectorBlob,
                profile.LastUpdated
            });

            _logger.LogInformation("用户画像已保存");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存用户画像失败");
            return Result.Failure(ex);
        }
    }

    private class ProfileRow
    {
        public int Id { get; set; }
        public byte[]? AcousticMeanVector { get; set; }
        public byte[]? DeepMeanVector { get; set; }
        public DateTime LastUpdated { get; set; }

        public UserProfile ToUserProfile() => new()
        {
            Id = Id,
            AcousticMeanVectorBlob = AcousticMeanVector,
            DeepMeanVectorBlob = DeepMeanVector,
            LastUpdated = LastUpdated
        };
    }
}
