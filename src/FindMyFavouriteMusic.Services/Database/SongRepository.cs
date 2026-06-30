using Dapper;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 歌曲仓储实现，基于 Dapper + SQLite 提供 Songs 表的 CRUD 操作。
/// </summary>
/// <remarks>
/// Dapper 使用模式约定：
/// - QuerySingleAsync：插入后返回单值（如自增 ID）；
/// - QueryFirstOrDefaultAsync：单行查询，无结果时返回 null；
/// - QueryAsync：多行查询，返回集合；
/// - ExecuteAsync：插入/更新/删除等无返回值操作。
/// 由于数据库列名（AcousticVector）与实体属性名（AcousticVectorBlob）不一致，
/// 引入 SongRow 内部类作为映射桥梁，避免在实体上施加 Dapper 特定特性。
/// </remarks>
public class SongRepository : ISongRepository
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<SongRepository> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">数据库配置（含连接字符串）</param>
    /// <param name="logger">日志记录器</param>
    public SongRepository(
        IOptions<DatabaseOptions> options,
        ILogger<SongRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 插入一首歌曲并返回数据库自增 ID。
    /// </summary>
    /// <param name="song">歌曲实体</param>
    /// <returns>新插入记录的 ID</returns>
    /// <inheritdoc/>
    public async Task<Result<int>> InsertAsync(Song song)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            // 插入后通过 last_insert_rowid() 取回 SQLite 自增 ID
            // IsLiked 在库中存为 INTEGER(0/1)，需要从 bool 转换
            var sql = """
                INSERT INTO Songs (FilePath, Title, Artist, IsLiked, AcousticVector, DeepVector)
                VALUES (@FilePath, @Title, @Artist, @IsLiked, @AcousticVectorBlob, @DeepVectorBlob);
                SELECT last_insert_rowid();
                """;

            var id = await connection.QuerySingleAsync<int>(sql, new
            {
                song.FilePath,
                song.Title,
                song.Artist,
                // bool → INTEGER：true=1, false=0
                IsLiked = song.IsLiked ? 1 : 0,
                song.AcousticVectorBlob,
                song.DeepVectorBlob
            });

            _logger.LogDebug("插入歌曲: {FilePath}, Id={Id}", song.FilePath, id);
            return Result<int>.Success(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "插入歌曲失败: {FilePath}", song.FilePath);
            return Result<int>.Failure(ex);
        }
    }

    /// <summary>
    /// 按文件路径查询歌曲（用于幂等性检查）。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <returns>匹配的歌曲实体，无匹配时为 null</returns>
    /// <inheritdoc/>
    public async Task<Result<Song?>> GetByFilePathAsync(string filePath)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Songs WHERE FilePath = @FilePath";
            // 映射到 SongRow 以解决列名/属性名不一致问题
            var row = await connection.QueryFirstOrDefaultAsync<SongRow>(sql, new { FilePath = filePath });

            return Result<Song?>.Success(row?.ToSong());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询歌曲失败: {FilePath}", filePath);
            return Result<Song?>.Failure(ex);
        }
    }

    /// <summary>
    /// 查询所有标记为喜欢的歌曲。
    /// </summary>
    /// <returns>喜欢的歌曲列表</returns>
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Song>>> GetLikedSongsAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Songs WHERE IsLiked = 1";
            var rows = await connection.QueryAsync<SongRow>(sql);
            var songs = rows.Select(r => r.ToSong()).ToList();

            return Result<IReadOnlyList<Song>>.Success(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询喜欢歌曲失败");
            return Result<IReadOnlyList<Song>>.Failure(ex);
        }
    }

    /// <summary>
    /// 查询库中全部歌曲，按 Id 升序返回。
    /// </summary>
    /// <returns>全部歌曲列表</returns>
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Song>>> GetAllSongsAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Songs ORDER BY Id";
            var rows = await connection.QueryAsync<SongRow>(sql);
            var songs = rows.Select(r => r.ToSong()).ToList();

            return Result<IReadOnlyList<Song>>.Success(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询所有歌曲失败");
            return Result<IReadOnlyList<Song>>.Failure(ex);
        }
    }

    /// <summary>
    /// 更新歌曲喜欢状态。
    /// </summary>
    /// <param name="id">歌曲 ID</param>
    /// <param name="isLiked">是否喜欢</param>
    /// <returns>操作结果</returns>
    /// <inheritdoc/>
    public async Task<Result> UpdateLikeStatusAsync(int id, bool isLiked)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "UPDATE Songs SET IsLiked = @IsLiked WHERE Id = @Id";
            // bool → INTEGER 转换
            await connection.ExecuteAsync(sql, new { IsLiked = isLiked ? 1 : 0, Id = id });

            _logger.LogInformation("更新歌曲喜欢状态: {SongId}, IsLiked={IsLiked}", id, isLiked);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新喜欢状态失败: {SongId}", id);
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 更新歌曲的特征向量 BLOB。
    /// </summary>
    /// <param name="id">歌曲 ID</param>
    /// <param name="acousticVectorBlob">声学特征向量 BLOB</param>
    /// <param name="deepVectorBlob">深度特征向量 BLOB</param>
    /// <returns>操作结果</returns>
    /// <remarks>
    /// 当前业务层未调用此方法，预留给未来增量更新场景（如批量补全历史歌曲特征向量）。
    /// </remarks>
    /// <inheritdoc/>
    public async Task<Result> UpdateVectorsAsync(int id, byte[]? acousticVectorBlob, byte[]? deepVectorBlob)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "UPDATE Songs SET AcousticVector = @AcousticVectorBlob, DeepVector = @DeepVectorBlob WHERE Id = @Id";
            await connection.ExecuteAsync(sql, new { AcousticVectorBlob = acousticVectorBlob, DeepVectorBlob = deepVectorBlob, Id = id });

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新特征向量失败: {SongId}", id);
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 按主键 ID 查询单首歌曲。
    /// </summary>
    /// <param name="id">歌曲 ID</param>
    /// <returns>歌曲实体（不存在时返回失败结果）</returns>
    /// <inheritdoc/>
    public async Task<Result<Song>> GetByIdAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Songs WHERE Id = @Id";
            var row = await connection.QueryFirstOrDefaultAsync<SongRow>(sql, new { Id = id });

            if (row is null)
            {
                return Result<Song>.Failure($"歌曲不存在: {id}");
            }

            return Result<Song>.Success(row.ToSong());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询歌曲失败: {SongId}", id);
            return Result<Song>.Failure(ex);
        }
    }

    /// <summary>
    /// Dapper 查询的行模型，字段名与数据库列名保持一致。
    /// </summary>
    /// <remarks>
    /// 作为数据库列（AcousticVector / DeepVector）与实体属性（AcousticVectorBlob / DeepVectorBlob）之间的映射桥梁，
    /// 避免在 Song 实体上使用 Dapper 的 Column 属性造成对持久化框架的耦合。
    /// </remarks>
    private class SongRow
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        // 数据库中以 INTEGER(0/1) 存储，与 C# bool 之间需要显式转换
        public int IsLiked { get; set; }
        public byte[]? AcousticVector { get; set; }
        public byte[]? DeepVector { get; set; }

        /// <summary>
        /// 将行模型转换为业务实体，完成列名映射与类型转换。
        /// </summary>
        /// <returns>业务实体</returns>
        public Song ToSong() => new()
        {
            Id = Id,
            FilePath = FilePath,
            Title = Title,
            Artist = Artist,
            // INTEGER(0/1) → bool：非零即视为 true
            IsLiked = IsLiked != 0,
            AcousticVectorBlob = AcousticVector,
            DeepVectorBlob = DeepVector
        };
    }
}
