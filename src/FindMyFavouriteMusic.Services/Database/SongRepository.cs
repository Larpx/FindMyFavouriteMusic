using Dapper;
using FindMyFavouriteMusic.Models.Entities;
using FindMyFavouriteMusic.Models.Results;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Services.Database;

/// <summary>
/// 歌曲仓储实现
/// </summary>
public class SongRepository : ISongRepository
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<SongRepository> _logger;

    public SongRepository(
        IOptions<DatabaseOptions> options,
        ILogger<SongRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<int>> InsertAsync(Song song)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

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

    /// <inheritdoc/>
    public async Task<Result<Song?>> GetByFilePathAsync(string filePath)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Songs WHERE FilePath = @FilePath";
            var row = await connection.QueryFirstOrDefaultAsync<SongRow>(sql, new { FilePath = filePath });

            return Result<Song?>.Success(row?.ToSong());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询歌曲失败: {FilePath}", filePath);
            return Result<Song?>.Failure(ex);
        }
    }

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

    /// <inheritdoc/>
    public async Task<Result> UpdateLikeStatusAsync(int id, bool isLiked)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            var sql = "UPDATE Songs SET IsLiked = @IsLiked WHERE Id = @Id";
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
    /// Dapper 查询的行模型，字段名与数据库列名一致
    /// </summary>
    private class SongRow
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public int IsLiked { get; set; }
        public byte[]? AcousticVector { get; set; }
        public byte[]? DeepVector { get; set; }

        public Song ToSong() => new()
        {
            Id = Id,
            FilePath = FilePath,
            Title = Title,
            Artist = Artist,
            IsLiked = IsLiked != 0,
            AcousticVectorBlob = AcousticVector,
            DeepVectorBlob = DeepVector
        };
    }
}
