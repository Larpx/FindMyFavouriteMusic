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
/// <remarks>
/// 单画像设计：UserProfile 表仅维护一行记录（Id 固定为 1），代表当前用户的全局偏好均值向量。
/// 通过 ON CONFLICT(Id) DO UPDATE 实现 upsert：存在则更新，不存在则插入，简化调用方逻辑。
/// 注意：仓储只负责填充 BLOB 字段（byte[]），不填充 float[] 字段；
/// float[] 向量的反序列化由调用方（如 PredictionService）按需执行，避免不必要的开销。
/// </remarks>
public class ProfileRepository
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<ProfileRepository> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">数据库配置（含连接字符串）</param>
    /// <param name="logger">日志记录器</param>
    public ProfileRepository(
        IOptions<DatabaseOptions> options,
        ILogger<ProfileRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 获取用户画像（单行表，Id 固定为 1）。
    /// </summary>
    /// <returns>用户画像实体；尚未构建时为 null</returns>
    public async Task<Result<UserProfile?>> GetAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            // 单画像设计：WHERE Id = 1 固定查询唯一行
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

    /// <summary>
    /// 保存用户画像（upsert 语义）。
    /// </summary>
    /// <param name="profile">用户画像实体</param>
    /// <returns>操作结果</returns>
    /// <remarks>
    /// 使用 SQLite 的 ON CONFLICT(Id) DO UPDATE 语法实现 upsert：
    /// 若 Id=1 的记录已存在，则更新各字段；否则插入新记录。
    /// 这样调用方无需关心画像是否已存在，简化了业务流程。
    /// </remarks>
    public async Task<Result> SaveAsync(UserProfile profile)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            // Id 固定为 1，确保单行表语义；ON CONFLICT 实现 upsert
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

    /// <summary>
    /// Dapper 查询的行模型，字段名与数据库列名保持一致。
    /// </summary>
    /// <remarks>
    /// 作为数据库列（AcousticMeanVector / DeepMeanVector）与实体属性
    /// （AcousticMeanVectorBlob / DeepMeanVectorBlob）之间的映射桥梁。
    /// 注意：仓储仅填充 BLOB 字段，float[] 向量字段由调用方按需反序列化。
    /// </remarks>
    private class ProfileRow
    {
        public int Id { get; set; }
        public byte[]? AcousticMeanVector { get; set; }
        public byte[]? DeepMeanVector { get; set; }
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// 将行模型转换为用户画像实体（仅填充 BLOB 字段，不反序列化向量）。
        /// </summary>
        /// <returns>用户画像实体</returns>
        public UserProfile ToUserProfile() => new()
        {
            Id = Id,
            AcousticMeanVectorBlob = AcousticMeanVector,
            DeepMeanVectorBlob = DeepMeanVector,
            LastUpdated = LastUpdated
        };
    }
}
