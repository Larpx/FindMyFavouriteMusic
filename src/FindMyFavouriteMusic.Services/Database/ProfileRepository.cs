using Dapper;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 用户画像仓储实现，基于 Dapper + SQLite 维护单行 UserProfile 表。
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

    public async Task<Result<UserProfile?>> GetAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var row = await connection.QueryFirstOrDefaultAsync<ProfileRow>(
                "SELECT * FROM UserProfile WHERE Id = 1");
            return Result<UserProfile?>.Success(row?.ToUserProfile());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户画像失败");
            return Result<UserProfile?>.Failure(ex);
        }
    }

    public async Task<Result> SaveAsync(UserProfile profile)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = """
                INSERT INTO UserProfile (Id, AcousticMeanVector, DeepMeanVector, AcousticSampleCount, DeepSampleCount, LastUpdated)
                VALUES (1, @AcousticMeanVectorBlob, @DeepMeanVectorBlob, @AcousticSampleCount, @DeepSampleCount, @LastUpdated)
                ON CONFLICT(Id) DO UPDATE SET
                    AcousticMeanVector = @AcousticMeanVectorBlob,
                    DeepMeanVector = @DeepMeanVectorBlob,
                    AcousticSampleCount = @AcousticSampleCount,
                    DeepSampleCount = @DeepSampleCount,
                    LastUpdated = @LastUpdated
                """;

            await connection.ExecuteAsync(sql, new
            {
                profile.AcousticMeanVectorBlob,
                profile.DeepMeanVectorBlob,
                profile.AcousticSampleCount,
                profile.DeepSampleCount,
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
        public int AcousticSampleCount { get; set; }
        public int DeepSampleCount { get; set; }
        public DateTime LastUpdated { get; set; }

        public UserProfile ToUserProfile() => new()
        {
            Id = Id,
            AcousticMeanVectorBlob = AcousticMeanVector,
            DeepMeanVectorBlob = DeepMeanVector,
            AcousticSampleCount = AcousticSampleCount,
            DeepSampleCount = DeepSampleCount,
            LastUpdated = LastUpdated
        };
    }
}
