using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Core.Prediction;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Models.Results;
using FindMyFavouriteMusic.Services.Database;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.Services;

/// <summary>
/// 预测服务，端到端编排：解码 -> 提取特征 -> 相似度 -> 分数
/// </summary>
public class PredictionService : IPredictionService
{
    private readonly IAudioDecoder _audioDecoder;
    private readonly IAcousticFeatureExtractor _acousticExtractor;
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly PredictionEngine _predictionEngine;
    private readonly ProfileRepository _profileRepository;
    private readonly ISongRepository _songRepository;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly FeatureExtractionOptions _featureOptions;
    private readonly ILogger<PredictionService> _logger;

    public PredictionService(
        IAudioDecoder audioDecoder,
        IAcousticFeatureExtractor acousticExtractor,
        IDeepFeatureExtractor deepExtractor,
        PredictionEngine predictionEngine,
        ProfileRepository profileRepository,
        ISongRepository songRepository,
        IVectorSerializer vectorSerializer,
        IOptions<FeatureExtractionOptions> featureOptions,
        ILogger<PredictionService> logger)
    {
        _audioDecoder = audioDecoder;
        _acousticExtractor = acousticExtractor;
        _deepExtractor = deepExtractor;
        _predictionEngine = predictionEngine;
        _profileRepository = profileRepository;
        _songRepository = songRepository;
        _vectorSerializer = vectorSerializer;
        _featureOptions = featureOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(string filePath, CancellationToken ct = default)
    {
        var profileResult = await _profileRepository.GetAsync();
        if (!profileResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(profileResult.Error!, profileResult.Exception);
        }

        var profile = profileResult.Value;
        if (profile?.AcousticMeanVectorBlob is null)
        {
            return Result<PredictionResult>.Failure("用户画像尚未构建，请先标记喜欢的歌曲");
        }

        var decodeResult = await _audioDecoder.DecodeAsync(filePath, ct);
        if (!decodeResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(decodeResult.Error!, decodeResult.Exception);
        }

        var samples = decodeResult.Value!;
        var acousticResult = _acousticExtractor.Extract(samples, _featureOptions.TargetSampleRate);
        if (!acousticResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(acousticResult.Error!, acousticResult.Exception);
        }

        float[]? deepVector = null;
        if (_deepExtractor.IsModelLoaded)
        {
            var deepResult = await _deepExtractor.ExtractAsync(samples, _featureOptions.TargetSampleRate, ct);
            if (deepResult.IsSuccess)
            {
                deepVector = deepResult.Value;
            }
        }

        var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob);
        float[]? profileDeep = profile.DeepMeanVectorBlob is not null
            ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob)
            : null;

        var prediction = _predictionEngine.Predict(
            acousticResult.Value!, deepVector, profileAcoustic, profileDeep);

        if (prediction.IsSuccess && prediction.Value is not null)
        {
            prediction.Value.SongTitle = Path.GetFileNameWithoutExtension(filePath);
        }

        return prediction;
    }

    /// <inheritdoc/>
    public async Task<Result<PredictionResult>> PredictAsync(int songId, CancellationToken ct = default)
    {
        var songResult = await _songRepository.GetByIdAsync(songId);
        if (!songResult.IsSuccess)
        {
            return Result<PredictionResult>.Failure(songResult.Error!, songResult.Exception);
        }

        var song = songResult.Value!;

        if (song.AcousticVectorBlob is not null)
        {
            var profileResult = await _profileRepository.GetAsync();
            if (!profileResult.IsSuccess || profileResult.Value?.AcousticMeanVector is null)
            {
                return Result<PredictionResult>.Failure("用户画像尚未构建");
            }

            var profile = profileResult.Value;
            var acousticVector = _vectorSerializer.Deserialize(song.AcousticVectorBlob);
            var profileAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob!);
            float[]? deepVector = song.DeepVectorBlob is not null
                ? _vectorSerializer.Deserialize(song.DeepVectorBlob) : null;
            float[]? profileDeep = profile.DeepMeanVectorBlob is not null
                ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob) : null;

            var prediction = _predictionEngine.Predict(acousticVector, deepVector, profileAcoustic, profileDeep);
            if (prediction.IsSuccess && prediction.Value is not null)
            {
                prediction.Value.SongTitle = song.Title ?? Path.GetFileNameWithoutExtension(song.FilePath);
            }
            return prediction;
        }

        return await PredictAsync(song.FilePath, ct);
    }
}
