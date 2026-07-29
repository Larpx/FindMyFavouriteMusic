using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class RecommendResultRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly RecommendResultRepository _repo;

    public RecommendResultRepositoryTests()
    {
        var dbName = $"rec_repo_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();

        new DatabaseInitializer(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<DatabaseInitializer>>())
            .StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _repo = new RecommendResultRepository(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<RecommendResultRepository>>());
    }

    [Fact]
    public async Task ReplaceBatch_ThenGet_OrdersByScoreDescending()
    {
        var date = "2026-07-29";
        var save = await _repo.ReplaceBatchAsync(
        [
            new RecommendResultRow
            {
                SourceId = MusicSourceIds.Netease,
                ExternalId = "1",
                Title = "low",
                RecommendDate = date,
                Score = 10,
                Status = "scored",
                FetchedAt = DateTime.UtcNow
            },
            new RecommendResultRow
            {
                SourceId = MusicSourceIds.Netease,
                ExternalId = "2",
                Title = "high",
                RecommendDate = date,
                Score = 90,
                Status = "scored",
                FetchedAt = DateTime.UtcNow
            },
            new RecommendResultRow
            {
                SourceId = MusicSourceIds.Netease,
                ExternalId = "3",
                Title = "failed",
                RecommendDate = date,
                Score = null,
                Status = "failed",
                FetchedAt = DateTime.UtcNow
            }
        ]);
        save.IsSuccess.Should().BeTrue();

        var got = await _repo.GetBySourceDateAsync(MusicSourceIds.Netease, date);
        got.IsSuccess.Should().BeTrue();
        got.Value!.Select(r => r.Title).Should().Equal("high", "low", "failed");
    }

    [Fact]
    public async Task ReplaceBatch_ReplacesSameSourceDate()
    {
        var date = "2026-07-28";
        await _repo.ReplaceBatchAsync(
        [
            new RecommendResultRow
            {
                SourceId = "netease",
                ExternalId = "old",
                Title = "old",
                RecommendDate = date,
                Score = 1,
                Status = "scored",
                FetchedAt = DateTime.UtcNow
            }
        ]);

        await _repo.ReplaceBatchAsync(
        [
            new RecommendResultRow
            {
                SourceId = "netease",
                ExternalId = "new",
                Title = "new",
                RecommendDate = date,
                Score = 2,
                Status = "scored",
                FetchedAt = DateTime.UtcNow
            }
        ]);

        var got = await _repo.GetBySourceDateAsync("netease", date);
        got.Value.Should().ContainSingle(r => r.ExternalId == "new");
        got.Value.Should().NotContain(r => r.ExternalId == "old");
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
    }
}
