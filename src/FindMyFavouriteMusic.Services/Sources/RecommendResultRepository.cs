using Dapper;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;

public sealed class RecommendResultRow
{
    public int Id { get; set; }
    public string SourceId { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? RecommendDate { get; set; }
    public string? Reason { get; set; }
    public double? Score { get; set; }
    public double? AcousticScore { get; set; }
    public double? DeepScore { get; set; }
    public int? Fee { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime? ScoredAt { get; set; }
}

public sealed class RecommendResultRepository
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<RecommendResultRepository> _logger;

    public RecommendResultRepository(IOptions<DatabaseOptions> options, ILogger<RecommendResultRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result> ReplaceBatchAsync(IReadOnlyList<RecommendResultRow> rows, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);

            if (rows.Count > 0)
            {
                var sourceId = rows[0].SourceId;
                var date = rows[0].RecommendDate;
                await connection.ExecuteAsync(
                    "DELETE FROM RecommendResults WHERE SourceId = @SourceId AND RecommendDate = @RecommendDate",
                    new { SourceId = sourceId, RecommendDate = date },
                    tx);
            }

            const string sql = """
                INSERT INTO RecommendResults (
                    SourceId, ExternalId, Title, Artist, Album, RecommendDate, Reason,
                    Score, AcousticScore, DeepScore, Fee, Status, ErrorMessage, FetchedAt, ScoredAt)
                VALUES (
                    @SourceId, @ExternalId, @Title, @Artist, @Album, @RecommendDate, @Reason,
                    @Score, @AcousticScore, @DeepScore, @Fee, @Status, @ErrorMessage, @FetchedAt, @ScoredAt)
                """;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                await connection.ExecuteAsync(sql, row, tx);
            }

            await tx.CommitAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入日推结果失败");
            return Result.Failure(ex);
        }
    }

    public async Task<Result<IReadOnlyList<RecommendResultRow>>> GetBySourceDateAsync(
        string sourceId, string recommendDate, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);
            var rows = (await connection.QueryAsync<RecommendResultRow>(
                """
                SELECT * FROM RecommendResults
                WHERE SourceId = @SourceId AND RecommendDate = @RecommendDate
                ORDER BY CASE WHEN Score IS NULL THEN 1 ELSE 0 END, Score DESC, Id
                """,
                new { SourceId = sourceId, RecommendDate = recommendDate })).ToList();
            return Result<IReadOnlyList<RecommendResultRow>>.Success(rows);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<RecommendResultRow>>.Failure(ex);
        }
    }
}
