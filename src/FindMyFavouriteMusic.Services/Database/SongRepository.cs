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
                INSERT INTO Songs (
                    FilePath, Title, Artist, IsLiked, AcousticVector, DeepVector,
                    FileMd5, FileSize, DurationMs, Format, AcousticDim, DeepModelType, DeepDim, FeatureExtractedAt,
                    Album, AlbumArtist, Genre, Year, Track, Disc, Comment, Lyrics)
                VALUES (
                    @FilePath, @Title, @Artist, @IsLiked, @AcousticVectorBlob, @DeepVectorBlob,
                    @FileMd5, @FileSize, @DurationMs, @Format, @AcousticDim, @DeepModelType, @DeepDim, @FeatureExtractedAt,
                    @Album, @AlbumArtist, @Genre, @Year, @Track, @Disc, @Comment, @Lyrics);
                SELECT last_insert_rowid();
                """;

            var id = await connection.QuerySingleAsync<int>(sql, ToInsertParams(song));
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

            var row = await connection.QueryFirstOrDefaultAsync<SongRow>(
                "SELECT * FROM Songs WHERE FilePath = @FilePath", new { FilePath = filePath });
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

            var rows = await connection.QueryAsync<SongRow>("SELECT * FROM Songs WHERE IsLiked = 1");
            return Result<IReadOnlyList<Song>>.Success(rows.Select(r => r.ToSong()).ToList());
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

            var rows = await connection.QueryAsync<SongRow>("SELECT * FROM Songs ORDER BY Id");
            return Result<IReadOnlyList<Song>>.Success(rows.Select(r => r.ToSong()).ToList());
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

            await connection.ExecuteAsync(
                "UPDATE Songs SET IsLiked = @IsLiked WHERE Id = @Id",
                new { IsLiked = isLiked ? 1 : 0, Id = id });

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

            await connection.ExecuteAsync(
                "UPDATE Songs SET AcousticVector = @AcousticVectorBlob, DeepVector = @DeepVectorBlob WHERE Id = @Id",
                new { AcousticVectorBlob = acousticVectorBlob, DeepVectorBlob = deepVectorBlob, Id = id });

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新特征向量失败: {SongId}", id);
            return Result.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateFeaturesAsync(Song song)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            const string sql = """
                UPDATE Songs SET
                    AcousticVector = @AcousticVectorBlob,
                    DeepVector = @DeepVectorBlob,
                    FileMd5 = @FileMd5,
                    FileSize = @FileSize,
                    DurationMs = @DurationMs,
                    Format = @Format,
                    AcousticDim = @AcousticDim,
                    DeepModelType = @DeepModelType,
                    DeepDim = @DeepDim,
                    FeatureExtractedAt = @FeatureExtractedAt,
                    Title = COALESCE(@Title, Title),
                    Artist = COALESCE(@Artist, Artist)
                WHERE Id = @Id
                """;

            await connection.ExecuteAsync(sql, new
            {
                song.Id,
                song.AcousticVectorBlob,
                song.DeepVectorBlob,
                song.FileMd5,
                song.FileSize,
                song.DurationMs,
                song.Format,
                song.AcousticDim,
                song.DeepModelType,
                song.DeepDim,
                song.FeatureExtractedAt,
                song.Title,
                song.Artist
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新歌曲特征失败: {SongId}", song.Id);
            return Result.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateFingerprintAsync(int id, string fileMd5, long fileSize)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            // 仅更新指纹：标签写回后 MD5 变但音频流通常不变，保留特征向量（B6）
            await connection.ExecuteAsync(
                "UPDATE Songs SET FileMd5 = @FileMd5, FileSize = @FileSize WHERE Id = @Id",
                new { FileMd5 = fileMd5, FileSize = fileSize, Id = id });

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新文件指纹失败: {SongId}", id);
            return Result.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateMetadataAsync(Song song)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync();

            const string sql = """
                UPDATE Songs SET
                    Title = @Title,
                    Artist = @Artist,
                    Album = @Album,
                    AlbumArtist = @AlbumArtist,
                    Genre = @Genre,
                    Year = @Year,
                    Track = @Track,
                    Disc = @Disc,
                    Comment = @Comment,
                    Lyrics = @Lyrics,
                    FileMd5 = @FileMd5,
                    FileSize = @FileSize,
                    DurationMs = COALESCE(@DurationMs, DurationMs)
                WHERE Id = @Id
                """;

            await connection.ExecuteAsync(sql, new
            {
                song.Id,
                song.Title,
                song.Artist,
                song.Album,
                song.AlbumArtist,
                song.Genre,
                song.Year,
                song.Track,
                song.Disc,
                song.Comment,
                song.Lyrics,
                song.FileMd5,
                song.FileSize,
                song.DurationMs
            });

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新元数据失败: {SongId}", song.Id);
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

            var row = await connection.QueryFirstOrDefaultAsync<SongRow>(
                "SELECT * FROM Songs WHERE Id = @Id", new { Id = id });

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

    private static object ToInsertParams(Song song) => new
    {
        song.FilePath,
        song.Title,
        song.Artist,
        IsLiked = song.IsLiked ? 1 : 0,
        song.AcousticVectorBlob,
        song.DeepVectorBlob,
        song.FileMd5,
        song.FileSize,
        song.DurationMs,
        song.Format,
        song.AcousticDim,
        song.DeepModelType,
        song.DeepDim,
        song.FeatureExtractedAt,
        song.Album,
        song.AlbumArtist,
        song.Genre,
        song.Year,
        song.Track,
        song.Disc,
        song.Comment,
        song.Lyrics
    };

    private class SongRow
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public int IsLiked { get; set; }
        public byte[]? AcousticVector { get; set; }
        public byte[]? DeepVector { get; set; }
        public string? FileMd5 { get; set; }
        public long? FileSize { get; set; }
        public int? DurationMs { get; set; }
        public string? Format { get; set; }
        public int? AcousticDim { get; set; }
        public string? DeepModelType { get; set; }
        public int? DeepDim { get; set; }
        public DateTime? FeatureExtractedAt { get; set; }
        public string? Album { get; set; }
        public string? AlbumArtist { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public string? Track { get; set; }
        public string? Disc { get; set; }
        public string? Comment { get; set; }
        public string? Lyrics { get; set; }

        public Song ToSong() => new()
        {
            Id = Id,
            FilePath = FilePath,
            Title = Title,
            Artist = Artist,
            IsLiked = IsLiked != 0,
            AcousticVectorBlob = AcousticVector,
            DeepVectorBlob = DeepVector,
            FileMd5 = FileMd5,
            FileSize = FileSize,
            DurationMs = DurationMs,
            Format = Format,
            AcousticDim = AcousticDim,
            DeepModelType = DeepModelType,
            DeepDim = DeepDim,
            FeatureExtractedAt = FeatureExtractedAt,
            Album = Album,
            AlbumArtist = AlbumArtist,
            Genre = Genre,
            Year = Year,
            Track = Track,
            Disc = Disc,
            Comment = Comment,
            Lyrics = Lyrics
        };
    }
}
