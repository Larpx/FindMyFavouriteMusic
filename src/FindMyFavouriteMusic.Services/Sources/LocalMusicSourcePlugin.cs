using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;

/// <summary>本地曲库适配为音乐源插件（喜欢=IsLiked；无远程日推）。</summary>
public sealed class LocalMusicSourcePlugin : IMusicSourcePlugin
{
    private readonly ISongRepository _songs;

    public LocalMusicSourcePlugin(ISongRepository songs)
    {
        _songs = songs;
    }

    public string Id => MusicSourceIds.Local;
    public string DisplayName => "本地音乐库";

    public MusicSourceCapabilities Capabilities { get; } = new()
    {
        SupportsLogin = false,
        SupportsLikedList = true,
        SupportsDailyRecommend = false,
        SupportsHistoryRecommend = false,
        SupportsStreamingDownload = false
    };

    public Task<Result<MusicSourceAuthState>> GetAuthStateAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<MusicSourceAuthState>.Success(new MusicSourceAuthState
        {
            IsAuthenticated = true,
            DisplayName = "本地",
            Message = "无需登录"
        }));

    public Task<Result<QrLoginSession>> BeginQrLoginAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<QrLoginSession>.Failure($"{MusicSourceErrorCodes.NotSupported}: 本地源不支持登录"));

    public Task<Result<QrLoginPollResult>> PollQrLoginAsync(string sessionKey, CancellationToken ct = default) =>
        Task.FromResult(Result<QrLoginPollResult>.Failure($"{MusicSourceErrorCodes.NotSupported}: 本地源不支持登录"));

    public Task<Result> SignInWithCookieAsync(string cookieHeader, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure($"{MusicSourceErrorCodes.NotSupported}: 本地源不支持登录"));

    public Task<Result> SignOutAsync(CancellationToken ct = default) =>
        Task.FromResult(Result.Success());

    public async Task<Result<IReadOnlyList<MusicTrackRef>>> GetLikedTracksAsync(CancellationToken ct = default)
    {
        var liked = await _songs.GetLikedSongsAsync();
        if (!liked.IsSuccess)
        {
            return Result<IReadOnlyList<MusicTrackRef>>.Failure(liked.Error!, liked.Exception);
        }

        var list = (liked.Value ?? [])
            .Select(s => new MusicTrackRef
            {
                SourceId = MusicSourceIds.Local,
                ExternalId = s.Id.ToString(),
                Title = s.Title,
                Artists = string.IsNullOrWhiteSpace(s.Artist) ? [] : [s.Artist],
                Album = s.Album,
                DurationMs = s.DurationMs
            })
            .ToList();

        return Result<IReadOnlyList<MusicTrackRef>>.Success(list);
    }

    public Task<Result<IReadOnlyList<MusicTrackRef>>> GetDailyRecommendAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<MusicTrackRef>>.Failure(
            $"{MusicSourceErrorCodes.NotSupported}: 本地源无日推"));

    public Task<Result<IReadOnlyList<string>>> GetHistoryRecommendDatesAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Failure(
            $"{MusicSourceErrorCodes.NotSupported}: 本地源无历史日推"));

    public Task<Result<IReadOnlyList<MusicTrackRef>>> GetHistoryRecommendAsync(string date, CancellationToken ct = default) =>
        Task.FromResult(Result<IReadOnlyList<MusicTrackRef>>.Failure(
            $"{MusicSourceErrorCodes.NotSupported}: 本地源无历史日推"));

    public async Task<Result<ResolvedAudio>> ResolveAudioAsync(
        MusicTrackRef track,
        bool preferTemporary = true,
        CancellationToken ct = default)
    {
        if (!int.TryParse(track.ExternalId, out var id))
        {
            return Result<ResolvedAudio>.Failure("无效的本地曲目 Id");
        }

        var song = await _songs.GetByIdAsync(id);
        if (!song.IsSuccess)
        {
            return Result<ResolvedAudio>.Failure(song.Error!, song.Exception);
        }

        var path = song.Value!.FilePath;
        if (!File.Exists(path))
        {
            return Result<ResolvedAudio>.Failure($"本地文件不存在: {path}");
        }

        return Result<ResolvedAudio>.Success(new ResolvedAudio
        {
            FilePath = path,
            IsTemporary = false,
            Format = song.Value.Format
        });
    }
}
