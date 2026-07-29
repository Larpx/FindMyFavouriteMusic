using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Helpers;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Sources;

public class LocalMusicSourcePluginTests : IDisposable
{
    private readonly string _wavPath;
    private readonly Mock<ISongRepository> _songs = new();
    private readonly LocalMusicSourcePlugin _plugin;

    public LocalMusicSourcePluginTests()
    {
        _wavPath = WavTestFile.CreateSilentWav();
        _plugin = new LocalMusicSourcePlugin(_songs.Object);
    }

    [Fact]
    public async Task GetAuthState_IsAlwaysAuthenticated()
    {
        var state = await _plugin.GetAuthStateAsync();
        state.IsSuccess.Should().BeTrue();
        state.Value!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task BeginQrLogin_IsNotSupported()
    {
        var result = await _plugin.BeginQrLoginAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(MusicSourceErrorCodes.NotSupported);
    }

    [Fact]
    public async Task GetLikedTracks_MapsLikedSongs()
    {
        _songs.Setup(s => s.GetLikedSongsAsync()).ReturnsAsync(Result<IReadOnlyList<Song>>.Success(
        [
            new Song { Id = 7, Title = "A", Artist = "Art", Album = "Alb", DurationMs = 1000 }
        ]));

        var liked = await _plugin.GetLikedTracksAsync();
        liked.IsSuccess.Should().BeTrue();
        liked.Value.Should().ContainSingle();
        liked.Value![0].SourceId.Should().Be(MusicSourceIds.Local);
        liked.Value[0].ExternalId.Should().Be("7");
        liked.Value[0].Title.Should().Be("A");
        liked.Value[0].Artists.Should().Equal("Art");
    }

    [Fact]
    public async Task GetDailyRecommend_IsNotSupported()
    {
        var result = await _plugin.GetDailyRecommendAsync();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(MusicSourceErrorCodes.NotSupported);
    }

    [Fact]
    public async Task ResolveAudioAsync_ReturnsExistingLocalFile_NotTemporary()
    {
        _songs.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(Result<Song>.Success(new Song
        {
            Id = 3,
            FilePath = _wavPath,
            Format = "Wav"
        }));

        var resolved = await _plugin.ResolveAudioAsync(new MusicTrackRef
        {
            SourceId = MusicSourceIds.Local,
            ExternalId = "3",
            Title = "t"
        });

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value!.FilePath.Should().Be(_wavPath);
        resolved.Value.IsTemporary.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAudioAsync_Fails_WhenFileMissing()
    {
        _songs.Setup(s => s.GetByIdAsync(9)).ReturnsAsync(Result<Song>.Success(new Song
        {
            Id = 9,
            FilePath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.wav")
        }));

        var resolved = await _plugin.ResolveAudioAsync(new MusicTrackRef
        {
            SourceId = MusicSourceIds.Local,
            ExternalId = "9"
        });

        resolved.IsSuccess.Should().BeFalse();
        resolved.Error.Should().Contain("不存在");
    }

    public void Dispose()
    {
        if (File.Exists(_wavPath))
        {
            File.Delete(_wavPath);
        }
    }
}
