using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// <see cref="PredictionService"/> 关键路径测试（含 PredictAsync(int) BLOB 检查修复）。
/// </summary>
public class PredictionServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ProfileRepository _profileRepo;
    private readonly Mock<ISongRepository> _songRepo = new();
    private readonly Mock<IAudioDecoder> _decoder = new();
    private readonly Mock<IAcousticFeatureExtractor> _acoustic = new();
    private readonly Mock<IDeepFeatureExtractor> _deep = new();
    private readonly VectorSerializer _serializer = new();
    private readonly PredictionService _service;

    public PredictionServiceTests()
    {
        var dbName = $"pred_{Guid.NewGuid():N}";
        var cs = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();

        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE UserProfile (
                    Id INTEGER PRIMARY KEY,
                    AcousticMeanVector BLOB,
                    DeepMeanVector BLOB,
                    AcousticSampleCount INTEGER DEFAULT 0,
                    DeepSampleCount INTEGER DEFAULT 0,
                    LastUpdated DATETIME
                );
                """;
            cmd.ExecuteNonQuery();
        }

        _profileRepo = new ProfileRepository(
            Options.Create(new DatabaseOptions { ConnectionString = cs }),
            Mock.Of<ILogger<ProfileRepository>>());

        var engine = new PredictionEngine(
            new CosineSimilarityCalculator(),
            _deep.Object,
            Options.Create(new PredictionOptions { AcousticWeight = 0.4, DeepWeight = 0.6, AcousticOnlyWeight = 1.0 }),
            Mock.Of<ILogger<PredictionEngine>>());

        _deep.SetupGet(d => d.IsModelLoaded).Returns(false);

        _service = new PredictionService(
            _decoder.Object,
            _acoustic.Object,
            _deep.Object,
            engine,
            _profileRepo,
            _songRepo.Object,
            _serializer,
            new ModelOperationLock(),
            Options.Create(new FeatureExtractionOptions { TargetSampleRate = 16000 }),
            Mock.Of<ILogger<PredictionService>>());
    }

    [Fact]
    public async Task PredictAsync_BySongId_UsesBlobProfile_WhenFloatArrayNull()
    {
        // Arrange: 仓储只填 BLOB（复现 A5 bug：误查 AcousticMeanVector）
        await _profileRepo.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _serializer.Serialize([1f, 0f]),
            AcousticSampleCount = 1,
            LastUpdated = DateTime.UtcNow
        });

        _songRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Result<Song>.Success(new Song
        {
            Id = 1,
            FilePath = "/x.mp3",
            Title = "Song",
            AcousticVectorBlob = _serializer.Serialize([1f, 0f])
        }));

        // Act
        var result = await _service.PredictAsync(1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.SongTitle.Should().Be("Song");
        _decoder.Verify(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PredictAsync_BySongId_NoProfile_ReturnsFailure()
    {
        _songRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(Result<Song>.Success(new Song
        {
            Id = 1,
            FilePath = "/x.mp3",
            AcousticVectorBlob = _serializer.Serialize([1f, 0f])
        }));

        var result = await _service.PredictAsync(1);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("画像");
    }

    [Fact]
    public async Task PredictAsync_ByFilePath_NoProfile_ReturnsFailure()
    {
        var result = await _service.PredictAsync("/missing.mp3");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task PredictAsync_ByFilePath_WithProfile_DecodesAndScores()
    {
        await _profileRepo.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _serializer.Serialize([1f, 0f]),
            LastUpdated = DateTime.UtcNow
        });

        _decoder.Setup(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success([0.1f, 0.2f]));
        _acoustic.Setup(a => a.Extract(It.IsAny<float[]>(), It.IsAny<int>()))
            .Returns(Result<float[]>.Success([1f, 0f]));

        var result = await _service.PredictAsync("/a.mp3");
        result.IsSuccess.Should().BeTrue();
        result.Value!.SongTitle.Should().Be("a");
    }

    [Fact]
    public async Task PredictBatchAsync_ReportsProgress()
    {
        await _profileRepo.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _serializer.Serialize([1f, 0f]),
            LastUpdated = DateTime.UtcNow
        });
        _decoder.Setup(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success([0.1f]));
        _acoustic.Setup(a => a.Extract(It.IsAny<float[]>(), It.IsAny<int>()))
            .Returns(Result<float[]>.Success([1f, 0f]));

        var last = -1;
        var progress = new Progress<int>(p => last = p);
        var results = await _service.PredictBatchAsync(["/a.mp3", "/b.mp3"], progress);
        results.Should().HaveCount(2);
        last.Should().Be(100);
    }

    [Fact]
    public async Task PredictWithProgressAsync_ReportsAndScores()
    {
        await _profileRepo.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _serializer.Serialize([1f, 0f]),
            LastUpdated = DateTime.UtcNow
        });
        _decoder.Setup(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success([0.1f]));
        _acoustic.Setup(a => a.Extract(It.IsAny<float[]>(), It.IsAny<int>()))
            .Returns(Result<float[]>.Success([1f, 0f]));

        var seen = new List<int>();
        var progress = new Progress<int>(p => seen.Add(p));
        var result = await _service.PredictWithProgressAsync("/solo.mp3", progress);
        result.IsSuccess.Should().BeTrue();
        seen.Should().NotBeEmpty();
        seen.Should().Contain(100);
    }

    [Fact]
    public async Task PredictAsync_BySongId_WithoutStoredFeatures_FallsBackToDecode()
    {
        await _profileRepo.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _serializer.Serialize([1f, 0f]),
            LastUpdated = DateTime.UtcNow
        });
        _songRepo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(Result<Song>.Success(new Song
        {
            Id = 9,
            FilePath = "/fallback.mp3",
            Title = "FB",
            AcousticVectorBlob = null
        }));
        _decoder.Setup(d => d.DecodeAsync("/fallback.mp3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success([0.1f]));
        _acoustic.Setup(a => a.Extract(It.IsAny<float[]>(), It.IsAny<int>()))
            .Returns(Result<float[]>.Success([1f, 0f]));

        var result = await _service.PredictAsync(9);
        result.IsSuccess.Should().BeTrue();
        _decoder.Verify(d => d.DecodeAsync("/fallback.mp3", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAlive);
        _keepAlive.Dispose();
    }
}
