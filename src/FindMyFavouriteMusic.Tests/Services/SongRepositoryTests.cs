using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// <see cref="SongRepository"/> 集成测试（内存 SQLite + schema v1）。
/// </summary>
public class SongRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SongRepository _repo;

    public SongRepositoryTests()
    {
        var dbName = $"song_repo_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();

        var initializer = new DatabaseInitializer(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<DatabaseInitializer>>());
        initializer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _repo = new SongRepository(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<SongRepository>>());
    }

    [Fact]
    public async Task Insert_GetById_RoundTrip_IncludesNewColumns()
    {
        var song = new Song
        {
            FilePath = "/a.mp3",
            Title = "t",
            Artist = "ar",
            FileMd5 = "abc",
            FileSize = 100,
            Format = "Mp3",
            AcousticDim = 52,
            DeepModelType = "MERT",
            DeepDim = 768,
            Album = "alb",
            Genre = "g",
            Year = 2020,
            Track = "1",
            AcousticVectorBlob = [1, 2, 3]
        };

        var insert = await _repo.InsertAsync(song);
        insert.IsSuccess.Should().BeTrue();

        var got = await _repo.GetByIdAsync(insert.Value);
        got.IsSuccess.Should().BeTrue();
        got.Value!.FileMd5.Should().Be("abc");
        got.Value.DeepModelType.Should().Be("MERT");
        got.Value.Album.Should().Be("alb");
        got.Value.AcousticVectorBlob.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task UpdateFingerprint_DoesNotClearVectors()
    {
        var song = new Song
        {
            FilePath = "/b.mp3",
            Title = "t",
            FileMd5 = "old",
            FileSize = 1,
            AcousticVectorBlob = [9, 9],
            DeepVectorBlob = [8, 8]
        };
        var id = (await _repo.InsertAsync(song)).Value;

        var fp = await _repo.UpdateFingerprintAsync(id, "newmd5", 999);
        fp.IsSuccess.Should().BeTrue();

        var got = await _repo.GetByIdAsync(id);
        got.Value!.FileMd5.Should().Be("newmd5");
        got.Value.FileSize.Should().Be(999);
        got.Value.AcousticVectorBlob.Should().Equal(9, 9);
        got.Value.DeepVectorBlob.Should().Equal(8, 8);
    }

    [Fact]
    public async Task UpdateMetadataAsync_UpdatesTextFieldsAndMd5()
    {
        var id = (await _repo.InsertAsync(new Song
        {
            FilePath = "/c.mp3",
            Title = "old",
            FileMd5 = "m1",
            AcousticVectorBlob = [1]
        })).Value;

        var result = await _repo.UpdateMetadataAsync(new Song
        {
            Id = id,
            Title = "new",
            Artist = "a",
            Album = "alb",
            FileMd5 = "m2",
            FileSize = 50,
            DurationMs = 1000
        });
        result.IsSuccess.Should().BeTrue();

        var got = await _repo.GetByIdAsync(id);
        got.Value!.Title.Should().Be("new");
        got.Value.Artist.Should().Be("a");
        got.Value.Album.Should().Be("alb");
        got.Value.FileMd5.Should().Be("m2");
        got.Value.AcousticVectorBlob.Should().Equal(1);
    }

    [Fact]
    public async Task UpdateFeaturesAsync_UpdatesVectorsAndContract()
    {
        var id = (await _repo.InsertAsync(new Song { FilePath = "/d.mp3", Title = "t" })).Value;
        var song = new Song
        {
            Id = id,
            AcousticVectorBlob = [1, 2],
            DeepVectorBlob = [3],
            FileMd5 = "x",
            FileSize = 10,
            Format = "Wav",
            AcousticDim = 2,
            DeepModelType = "VGGish",
            DeepDim = 1,
            FeatureExtractedAt = DateTime.UtcNow,
            Title = "t2"
        };

        (await _repo.UpdateFeaturesAsync(song)).IsSuccess.Should().BeTrue();
        var got = await _repo.GetByIdAsync(id);
        got.Value!.DeepModelType.Should().Be("VGGish");
        got.Value.AcousticDim.Should().Be(2);
        got.Value.FileMd5.Should().Be("x");
    }

    [Fact]
    public async Task UpdateVectorsAsync_UpdatesBlobsOnly()
    {
        var id = (await _repo.InsertAsync(new Song
        {
            FilePath = "/vec.mp3",
            Title = "t",
            AcousticVectorBlob = [1],
            FileMd5 = "keep"
        })).Value;

        (await _repo.UpdateVectorsAsync(id, [9, 9], [8])).IsSuccess.Should().BeTrue();
        var got = await _repo.GetByIdAsync(id);
        got.Value!.AcousticVectorBlob.Should().Equal(9, 9);
        got.Value.DeepVectorBlob.Should().Equal(8);
        got.Value.FileMd5.Should().Be("keep");
    }

    [Fact]
    public async Task GetByFilePath_GetLiked_GetAll_Work()
    {
        var s1 = new Song { FilePath = "/e1.mp3", Title = "1", IsLiked = true };
        var s2 = new Song { FilePath = "/e2.mp3", Title = "2", IsLiked = false };
        await _repo.InsertAsync(s1);
        await _repo.InsertAsync(s2);

        var byPath = await _repo.GetByFilePathAsync("/e1.mp3");
        byPath.Value!.Title.Should().Be("1");

        var liked = await _repo.GetLikedSongsAsync();
        liked.Value.Should().HaveCount(1);

        var all = await _repo.GetAllSongsAsync();
        all.Value.Should().HaveCount(2);

        await _repo.UpdateLikeStatusAsync(byPath.Value.Id, false);
        liked = await _repo.GetLikedSongsAsync();
        liked.Value.Should().BeEmpty();
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
    }
}
