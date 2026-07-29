using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class MusicSourceOrchestratorTests : IDisposable
{
    private const string SourceId = "netease";
    private readonly SqliteConnection _keepAlive;
    private readonly RecommendResultRepository _recommendRepo;
    private readonly Mock<IMusicSourcePlugin> _plugin = new();
    private readonly Mock<ISongRepository> _songs = new();
    private readonly Mock<IMusicLibraryService> _library = new();
    private readonly Mock<IProfileService> _profile = new();
    private readonly Mock<IPredictionService> _prediction = new();
    private readonly Mock<IAudioDecoder> _decoder = new();
    private readonly Mock<IAcousticFeatureExtractor> _acoustic = new();
    private readonly Mock<IDeepFeatureExtractor> _deep = new();
    private readonly Mock<IVectorSerializer> _vectors = new();
    private readonly MusicSourceOrchestrator _orchestrator;

    public MusicSourceOrchestratorTests()
    {
        var dbName = $"orch_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();

        new DatabaseInitializer(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<DatabaseInitializer>>())
            .StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        _recommendRepo = new RecommendResultRepository(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<RecommendResultRepository>>());

        _plugin.SetupGet(p => p.Id).Returns(SourceId);
        var registry = new MusicSourceRegistry([_plugin.Object]);

        _deep.SetupGet(d => d.IsModelLoaded).Returns(false);
        _vectors.Setup(v => v.Serialize(It.IsAny<float[]>())).Returns([1, 2, 3]);

        _orchestrator = new MusicSourceOrchestrator(
            registry,
            _songs.Object,
            _library.Object,
            _profile.Object,
            _prediction.Object,
            _decoder.Object,
            _acoustic.Object,
            _deep.Object,
            _vectors.Object,
            new ModelOperationLock(),
            Options.Create(new FeatureExtractionOptions { TargetSampleRate = 16000 }),
            _recommendRepo,
            Mock.Of<ILogger<MusicSourceOrchestrator>>());
    }

    [Fact]
    public async Task ImportLikedAsync_Fails_WhenSourceUnknown()
    {
        var result = await _orchestrator.ImportLikedAsync("qq");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未知音乐源");
    }

    [Fact]
    public async Task ImportLikedAsync_MatchesLocalByTitleArtist_AndMarksLiked()
    {
        var track = new MusicTrackRef
        {
            SourceId = SourceId,
            ExternalId = "100",
            Title = "Match Me",
            Artists = ["Artist"]
        };
        _plugin.Setup(p => p.GetLikedTracksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<MusicTrackRef>>.Success([track]));
        _songs.Setup(s => s.GetBySourceExternalIdAsync(SourceId, "100"))
            .ReturnsAsync(Result<Song?>.Success(null));
        _songs.Setup(s => s.FindByTitleArtistAsync("Match Me", "Artist"))
            .ReturnsAsync(Result<Song?>.Success(new Song { Id = 5, Title = "Match Me", Artist = "Artist", IsLiked = false }));
        _songs.Setup(s => s.UpdateSourceAsync(5, SourceId, "100")).ReturnsAsync(Result.Success());
        _library.Setup(l => l.ToggleLikeAsync(5, true)).ReturnsAsync(Result.Success());

        var result = await _orchestrator.ImportLikedAsync(SourceId);

        result.IsSuccess.Should().BeTrue();
        _songs.Verify(s => s.UpdateSourceAsync(5, SourceId, "100"), Times.Once);
        _library.Verify(l => l.ToggleLikeAsync(5, true), Times.Once);
        _plugin.Verify(p => p.ResolveAudioAsync(It.IsAny<MusicTrackRef>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportLikedAsync_DownloadsTemp_Ingests_ThenDeletesFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"liked_{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tempPath, [1, 2, 3, 4]);

        var track = new MusicTrackRef
        {
            SourceId = SourceId,
            ExternalId = "200",
            Title = "Remote",
            Artists = ["R"]
        };
        _plugin.Setup(p => p.GetLikedTracksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<MusicTrackRef>>.Success([track]));
        _songs.Setup(s => s.GetBySourceExternalIdAsync(SourceId, "200"))
            .ReturnsAsync(Result<Song?>.Success(null));
        _songs.Setup(s => s.FindByTitleArtistAsync("Remote", "R"))
            .ReturnsAsync(Result<Song?>.Success(null));
        _plugin.Setup(p => p.ResolveAudioAsync(track, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedAudio>.Success(new ResolvedAudio
            {
                FilePath = tempPath,
                IsTemporary = true
            }));

        _decoder.Setup(d => d.DecodeAsync(tempPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new float[16000]));
        _acoustic.Setup(a => a.Extract(It.IsAny<float[]>(), 16000))
            .Returns(Result<float[]>.Success([0.1f, 0.2f]));
        _songs.Setup(s => s.GetByFilePathAsync($"{SourceId}://200"))
            .ReturnsAsync(Result<Song?>.Success(null));
        _songs.Setup(s => s.InsertAsync(It.IsAny<Song>()))
            .ReturnsAsync(Result<int>.Success(42));
        _profile.Setup(p => p.UpdateProfileIncrementalAsync(42))
            .ReturnsAsync(Result.Success());

        var result = await _orchestrator.ImportLikedAsync(SourceId);

        result.IsSuccess.Should().BeTrue();
        File.Exists(tempPath).Should().BeFalse("临时文件应在 Dispose 后删除");
        _songs.Verify(s => s.InsertAsync(It.Is<Song>(song =>
            song.IsLiked
            && song.SourceId == SourceId
            && song.ExternalId == "200"
            && song.FilePath == $"{SourceId}://200")), Times.Once);
        _profile.Verify(p => p.UpdateProfileIncrementalAsync(42), Times.Once);
    }

    [Fact]
    public async Task FetchAndScoreRecommendAsync_Scores_SortsDescending_Persists_AndDeletesTemp()
    {
        var tempA = Path.Combine(Path.GetTempPath(), $"rec_a_{Guid.NewGuid():N}.mp3");
        var tempB = Path.Combine(Path.GetTempPath(), $"rec_b_{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tempA, [1]);
        await File.WriteAllBytesAsync(tempB, [2]);

        var tracks = new List<MusicTrackRef>
        {
            new() { SourceId = SourceId, ExternalId = "a", Title = "Low", Artists = ["x"] },
            new() { SourceId = SourceId, ExternalId = "b", Title = "High", Artists = ["y"] }
        };
        _plugin.Setup(p => p.GetDailyRecommendAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<MusicTrackRef>>.Success(tracks));
        _plugin.Setup(p => p.ResolveAudioAsync(tracks[0], true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedAudio>.Success(new ResolvedAudio { FilePath = tempA, IsTemporary = true }));
        _plugin.Setup(p => p.ResolveAudioAsync(tracks[1], true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ResolvedAudio>.Success(new ResolvedAudio { FilePath = tempB, IsTemporary = true }));
        _prediction.Setup(p => p.PredictAsync(tempA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PredictionResult>.Success(new PredictionResult { Score = 40, AcousticScore = 40 }));
        _prediction.Setup(p => p.PredictAsync(tempB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PredictionResult>.Success(new PredictionResult { Score = 88, AcousticScore = 88 }));

        var result = await _orchestrator.FetchAndScoreRecommendAsync(SourceId);

        result.IsSuccess.Should().BeTrue();
        var rows = result.Value!;
        rows.Select(r => r.Title).Should().Equal("High", "Low");
        rows.Select(r => r.Score).Should().Equal(88d, 40d);
        File.Exists(tempA).Should().BeFalse();
        File.Exists(tempB).Should().BeFalse();

        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var saved = await _recommendRepo.GetBySourceDateAsync(SourceId, date);
        saved.Value!.Select(r => r.Title).Should().Equal("High", "Low");
    }

    [Fact]
    public async Task FetchAndScoreRecommendAsync_UsesHistoryApi_WhenDateProvided()
    {
        _plugin.Setup(p => p.GetHistoryRecommendAsync("2026-07-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<MusicTrackRef>>.Success([]));

        var result = await _orchestrator.FetchAndScoreRecommendAsync(SourceId, "2026-07-01");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _plugin.Verify(p => p.GetDailyRecommendAsync(It.IsAny<CancellationToken>()), Times.Never);
        _plugin.Verify(p => p.GetHistoryRecommendAsync("2026-07-01", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
    }
}
