using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Models.Entities;
using FindMyFavouriteMusic.Models.Results;
using FindMyFavouriteMusic.Services.Database;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Services;

/// <summary>
/// 音乐库管理服务
/// </summary>
public class MusicLibraryService : IMusicLibraryService
{
    private readonly IAudioDecoder _audioDecoder;
    private readonly IAcousticFeatureExtractor _acousticExtractor;
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly ISongRepository _songRepository;
    private readonly IProfileService _profileService;
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly ScanOptions _scanOptions;
    private readonly ILogger<MusicLibraryService> _logger;
    private readonly SemaphoreSlim _semaphore;

    public MusicLibraryService(
        IAudioDecoder audioDecoder,
        IAcousticFeatureExtractor acousticExtractor,
        IDeepFeatureExtractor deepExtractor,
        IVectorSerializer vectorSerializer,
        ISongRepository songRepository,
        IProfileService profileService,
        IOptions<FeatureExtractionOptions> featureOptions,
        IOptions<ScanOptions> scanOptions,
        ILogger<MusicLibraryService> logger)
    {
        _audioDecoder = audioDecoder;
        _acousticExtractor = acousticExtractor;
        _deepExtractor = deepExtractor;
        _vectorSerializer = vectorSerializer;
        _songRepository = songRepository;
        _profileService = profileService;
        _featureOptions = featureOptions.Value;
        _scanOptions = scanOptions.Value;
        _logger = logger;
        _semaphore = new SemaphoreSlim(_scanOptions.MaxConcurrentProcessing);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> ScanDirectoryAsync(
        string directoryPath, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Result<IReadOnlyList<SongDto>>.Failure($"目录不存在: {directoryPath}");
        }

        try
        {
            var files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => _scanOptions.SupportedExtensions.Contains(
                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                return Result<IReadOnlyList<SongDto>>.Success([]);
            }

            _logger.LogInformation("扫描到 {Count} 个音频文件", files.Count);

            var processed = 0;
            var songs = new List<SongDto>();

            var tasks = files.Select(async file =>
            {
                await _semaphore.WaitAsync(ct);
                try
                {
                    var result = await ProcessSongAsync(file, ct);
                    if (result.IsSuccess && result.Value is not null)
                    {
                        lock (songs)
                        {
                            songs.Add(result.Value);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("处理文件失败: {FilePath}, {Error}", file, result.Error);
                    }

                    Interlocked.Increment(ref processed);
                    progress?.Report((int)((double)processed / files.Count * 100));
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return Result<IReadOnlyList<SongDto>>.Success(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扫描目录失败: {DirectoryPath}", directoryPath);
            return Result<IReadOnlyList<SongDto>>.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> ToggleLikeAsync(int songId, bool isLiked)
    {
        var result = await _songRepository.UpdateLikeStatusAsync(songId, isLiked);
        if (!result.IsSuccess)
        {
            return result;
        }

        if (isLiked)
        {
            var updateResult = await _profileService.UpdateProfileIncrementalAsync(songId);
            if (!updateResult.IsSuccess)
            {
                _logger.LogWarning("画像增量更新失败: {Error}", updateResult.Error);
            }
        }
        else
        {
            var rebuildResult = await _profileService.RebuildProfileAsync();
            if (!rebuildResult.IsSuccess)
            {
                _logger.LogWarning("画像重建失败: {Error}", rebuildResult.Error);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> GetLikedSongsAsync()
    {
        var result = await _songRepository.GetLikedSongsAsync();
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<SongDto>>.Failure(result.Error!, result.Exception);
        }

        var dtos = (result.Value ?? []).Select(MapToDto).ToList();
        return Result<IReadOnlyList<SongDto>>.Success(dtos);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SongDto>>> GetAllSongsAsync()
    {
        var result = await _songRepository.GetAllSongsAsync();
        if (!result.IsSuccess)
        {
            return Result<IReadOnlyList<SongDto>>.Failure(result.Error!, result.Exception);
        }

        var dtos = (result.Value ?? []).Select(MapToDto).ToList();
        return Result<IReadOnlyList<SongDto>>.Success(dtos);
    }

    /// <inheritdoc/>
    public async Task<Result<SongDto>> ProcessSongAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var existingResult = await _songRepository.GetByFilePathAsync(filePath);
            if (existingResult.IsSuccess && existingResult.Value is not null)
            {
                return Result<SongDto>.Success(MapToDto(existingResult.Value));
            }

            var song = new Song
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = null,
                IsLiked = false
            };

            byte[]? acousticBlob = null;
            byte[]? deepBlob = null;

            var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
            if (decodeResult.IsSuccess && decodeResult.Value is not null)
            {
                var samples = decodeResult.Value;
                var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
                if (acousticResult.IsSuccess && acousticResult.Value is not null)
                {
                    acousticBlob = _vectorSerializer.Serialize(acousticResult.Value);
                    song.AcousticVector = acousticResult.Value;
                    song.AcousticVectorBlob = acousticBlob;
                }

                if (_deepExtractor.IsModelLoaded)
                {
                    var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
                    if (deepResult.IsSuccess && deepResult.Value is not null)
                    {
                        deepBlob = _vectorSerializer.Serialize(deepResult.Value);
                        song.DeepVector = deepResult.Value;
                        song.DeepVectorBlob = deepBlob;
                    }
                }
            }

            var insertResult = await _songRepository.InsertAsync(song);
            if (!insertResult.IsSuccess)
            {
                return Result<SongDto>.Failure(insertResult.Error!, insertResult.Exception);
            }

            song.Id = insertResult.Value;
            return Result<SongDto>.Success(MapToDto(song));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理歌曲失败: {FilePath}", filePath);
            return Result<SongDto>.Failure(ex);
        }
    }

    private static SongDto MapToDto(Song song) => new()
    {
        Id = song.Id,
        FilePath = song.FilePath,
        Title = song.Title,
        Artist = song.Artist,
        IsLiked = song.IsLiked,
        HasFeatures = song.AcousticVectorBlob is not null
    };
}
