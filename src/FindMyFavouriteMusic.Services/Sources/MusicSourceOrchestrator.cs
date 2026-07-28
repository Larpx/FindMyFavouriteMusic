using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;

/// <summary>跨源编排：红心导入（临时下载）与日推打分。</summary>
public sealed class MusicSourceOrchestrator : IMusicSourceOrchestrator
{
    private readonly IMusicSourceRegistry _registry;
    private readonly ISongRepository _songs;
    private readonly IMusicLibraryService _library;
    private readonly IProfileService _profile;
    private readonly IPredictionService _prediction;
    private readonly IAudioDecoder _decoder;
    private readonly IAcousticFeatureExtractor _acoustic;
    private readonly IDeepFeatureExtractor _deep;
    private readonly IVectorSerializer _vectors;
    private readonly IModelOperationLock _modelLock;
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly RecommendResultRepository _recommendRepo;
    private readonly ILogger<MusicSourceOrchestrator> _logger;

    public MusicSourceOrchestrator(
        IMusicSourceRegistry registry,
        ISongRepository songs,
        IMusicLibraryService library,
        IProfileService profile,
        IPredictionService prediction,
        IAudioDecoder decoder,
        IAcousticFeatureExtractor acoustic,
        IDeepFeatureExtractor deep,
        IVectorSerializer vectors,
        IModelOperationLock modelLock,
        IOptions<FeatureExtractionOptions> featureOptions,
        RecommendResultRepository recommendRepo,
        ILogger<MusicSourceOrchestrator> logger)
    {
        _registry = registry;
        _songs = songs;
        _library = library;
        _profile = profile;
        _prediction = prediction;
        _decoder = decoder;
        _acoustic = acoustic;
        _deep = deep;
        _vectors = vectors;
        _modelLock = modelLock;
        _featureOptions = featureOptions.Value;
        _recommendRepo = recommendRepo;
        _logger = logger;
    }

    public async Task<Result> ImportLikedAsync(
        string sourceId,
        IProgress<LikedImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plugin = _registry.TryGet(sourceId);
        if (plugin is null)
        {
            return Result.Failure($"未知音乐源: {sourceId}");
        }

        var liked = await plugin.GetLikedTracksAsync(ct);
        if (!liked.IsSuccess)
        {
            return Result.Failure(liked.Error!, liked.Exception);
        }

        var tracks = liked.Value ?? [];
        var matched = 0;
        var downloaded = 0;
        var failed = 0;

        for (var i = 0; i < tracks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var track = tracks[i];
            progress?.Report(new LikedImportProgress
            {
                Total = tracks.Count,
                Processed = i,
                MatchedLocal = matched,
                DownloadedTemp = downloaded,
                Failed = failed,
                CurrentTitle = track.Title
            });

            try
            {
                var existingExt = await _songs.GetBySourceExternalIdAsync(sourceId, track.ExternalId);
                if (existingExt.IsSuccess && existingExt.Value is { } already)
                {
                    if (!already.IsLiked)
                    {
                        await _library.ToggleLikeAsync(already.Id, true);
                    }

                    matched++;
                    continue;
                }

                var artist = track.Artists.FirstOrDefault();
                var local = await _songs.FindByTitleArtistAsync(track.Title ?? "", artist);
                if (local.IsSuccess && local.Value is { } localSong)
                {
                    await _songs.UpdateSourceAsync(localSong.Id, sourceId, track.ExternalId);
                    if (!localSong.IsLiked)
                    {
                        await _library.ToggleLikeAsync(localSong.Id, true);
                    }

                    matched++;
                    continue;
                }

                var resolve = await plugin.ResolveAudioAsync(track, preferTemporary: true, ct);
                if (!resolve.IsSuccess || resolve.Value is null)
                {
                    failed++;
                    _logger.LogWarning("红心下载失败: {Title} {Error}", track.Title, resolve.Error);
                    continue;
                }

                await using var resolved = resolve.Value;
                var ingest = await IngestTempAsLikedAsync(sourceId, track, resolved.FilePath, ct);
                if (ingest.IsSuccess)
                {
                    downloaded++;
                }
                else
                {
                    failed++;
                    _logger.LogWarning("红心入库失败: {Title} {Error}", track.Title, ingest.Error);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "导入红心异常: {Title}", track.Title);
            }
        }

        progress?.Report(new LikedImportProgress
        {
            Total = tracks.Count,
            Processed = tracks.Count,
            MatchedLocal = matched,
            DownloadedTemp = downloaded,
            Failed = failed
        });

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<RecommendResultRow>>> FetchAndScoreRecommendAsync(
        string sourceId,
        string? historyDate = null,
        IProgress<RecommendScoreProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plugin = _registry.TryGet(sourceId);
        if (plugin is null)
        {
            return Result<IReadOnlyList<RecommendResultRow>>.Failure($"未知音乐源: {sourceId}");
        }

        Result<IReadOnlyList<MusicTrackRef>> tracksResult;
        string recommendDate;
        if (string.IsNullOrWhiteSpace(historyDate))
        {
            tracksResult = await plugin.GetDailyRecommendAsync(ct);
            recommendDate = DateTime.Now.ToString("yyyy-MM-dd");
        }
        else
        {
            tracksResult = await plugin.GetHistoryRecommendAsync(historyDate, ct);
            recommendDate = historyDate;
        }

        if (!tracksResult.IsSuccess)
        {
            return Result<IReadOnlyList<RecommendResultRow>>.Failure(tracksResult.Error!, tracksResult.Exception);
        }

        var tracks = tracksResult.Value ?? [];
        var rows = new List<RecommendResultRow>(tracks.Count);
        var fetchedAt = DateTime.UtcNow;

        for (var i = 0; i < tracks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var track = tracks[i];
            progress?.Report(new RecommendScoreProgress
            {
                Total = tracks.Count,
                Processed = i,
                CurrentTitle = track.Title
            });

            var row = new RecommendResultRow
            {
                SourceId = sourceId,
                ExternalId = track.ExternalId,
                Title = track.Title,
                Artist = string.Join('/', track.Artists),
                Album = track.Album,
                RecommendDate = recommendDate,
                Reason = track.RecommendReason,
                Fee = track.Fee,
                FetchedAt = fetchedAt,
                Status = "pending"
            };

            try
            {
                var resolve = await plugin.ResolveAudioAsync(track, preferTemporary: true, ct);
                if (!resolve.IsSuccess || resolve.Value is null)
                {
                    row.Status = "failed";
                    row.ErrorMessage = resolve.Error;
                    rows.Add(row);
                    continue;
                }

                await using var audio = resolve.Value;
                var pred = await _prediction.PredictAsync(audio.FilePath, ct);
                if (!pred.IsSuccess || pred.Value is null)
                {
                    row.Status = "failed";
                    row.ErrorMessage = pred.Error;
                }
                else
                {
                    row.Status = "scored";
                    row.Score = pred.Value.Score;
                    row.AcousticScore = pred.Value.AcousticScore;
                    row.DeepScore = pred.Value.DeepScore;
                    row.ScoredAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                row.Status = "failed";
                row.ErrorMessage = ex.Message;
            }

            rows.Add(row);
        }

        rows.Sort((a, b) =>
        {
            var ascore = a.Score ?? double.MinValue;
            var bscore = b.Score ?? double.MinValue;
            return bscore.CompareTo(ascore);
        });

        var save = await _recommendRepo.ReplaceBatchAsync(rows, ct);
        if (!save.IsSuccess)
        {
            return Result<IReadOnlyList<RecommendResultRow>>.Failure(save.Error!, save.Exception);
        }

        progress?.Report(new RecommendScoreProgress { Total = tracks.Count, Processed = tracks.Count });
        return Result<IReadOnlyList<RecommendResultRow>>.Success(rows);
    }

    private async Task<Result> IngestTempAsLikedAsync(
        string sourceId, MusicTrackRef track, string filePath, CancellationToken ct)
    {
        await using var gate = await _modelLock.AcquireAsync(ct);

        var decode = await _decoder.DecodeAsync(filePath, ct);
        if (!decode.IsSuccess || decode.Value is null)
        {
            return Result.Failure(decode.Error!, decode.Exception);
        }

        var samples = decode.Value;
        var sampleRate = _featureOptions.TargetSampleRate;
        var acoustic = _acoustic.Extract(samples, sampleRate);
        if (!acoustic.IsSuccess || acoustic.Value is null)
        {
            return Result.Failure(acoustic.Error!, acoustic.Exception);
        }

        byte[]? deepBlob = null;
        int? deepDim = null;
        string? deepType = null;
        if (_deep.IsModelLoaded)
        {
            var deep = await _deep.ExtractAsync(samples, sampleRate, ct);
            if (deep.IsSuccess && deep.Value is not null)
            {
                deepBlob = _vectors.Serialize(deep.Value);
                deepDim = deep.Value.Length;
                deepType = _deep.ModelType.ToString();
            }
        }

        var placeholderPath = $"{sourceId}://{track.ExternalId}";
        var song = new Song
        {
            FilePath = placeholderPath,
            Title = track.Title,
            Artist = string.Join('/', track.Artists),
            Album = track.Album,
            DurationMs = track.DurationMs ?? (int)(samples.Length / (double)sampleRate * 1000),
            IsLiked = true,
            SourceId = sourceId,
            ExternalId = track.ExternalId,
            AcousticVectorBlob = _vectors.Serialize(acoustic.Value),
            AcousticDim = acoustic.Value.Length,
            DeepVectorBlob = deepBlob,
            DeepDim = deepDim,
            DeepModelType = deepType,
            FeatureExtractedAt = DateTime.UtcNow,
            Format = "Mp3",
            FileSize = new FileInfo(filePath).Length
        };

        var existing = await _songs.GetByFilePathAsync(placeholderPath);
        int songId;
        if (existing.IsSuccess && existing.Value is { } exSong)
        {
            song.Id = exSong.Id;
            await _songs.UpdateFeaturesAsync(song);
            await _songs.UpdateLikeStatusAsync(exSong.Id, true);
            await _songs.UpdateSourceAsync(exSong.Id, sourceId, track.ExternalId);
            songId = exSong.Id;
            return await _profile.UpdateProfileIncrementalAsync(songId);
        }

        var insert = await _songs.InsertAsync(song);
        if (!insert.IsSuccess)
        {
            return Result.Failure(insert.Error!, insert.Exception);
        }

        songId = insert.Value;
        return await _profile.UpdateProfileIncrementalAsync(songId);
    }
}
