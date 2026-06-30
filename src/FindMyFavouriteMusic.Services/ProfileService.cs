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
/// 用户画像服务，负责画像构建与增量更新
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ISongRepository _songRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly IVectorSerializer _vectorSerializer;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        ISongRepository songRepository,
        ProfileRepository profileRepository,
        IVectorSerializer vectorSerializer,
        IOptions<DatabaseOptions> dbOptions,
        ILogger<ProfileService> logger)
    {
        _songRepository = songRepository;
        _profileRepository = profileRepository;
        _vectorSerializer = vectorSerializer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ProfileDto>> GetProfileAsync()
    {
        var profileResult = await _profileRepository.GetAsync();
        if (!profileResult.IsSuccess)
        {
            return Result<ProfileDto>.Failure(profileResult.Error!, profileResult.Exception);
        }

        var likedResult = await _songRepository.GetLikedSongsAsync();
        if (!likedResult.IsSuccess)
        {
            return Result<ProfileDto>.Failure(likedResult.Error!, likedResult.Exception);
        }

        var profile = profileResult.Value;
        var likedSongs = likedResult.Value ?? [];

        return Result<ProfileDto>.Success(new ProfileDto
        {
            LikedSongCount = likedSongs.Count,
            LastUpdated = profile?.LastUpdated ?? DateTime.MinValue,
            HasDeepProfile = profile?.DeepMeanVectorBlob is not null
        });
    }

    /// <inheritdoc/>
    public async Task<Result> RebuildProfileAsync()
    {
        var likedResult = await _songRepository.GetLikedSongsAsync();
        if (!likedResult.IsSuccess)
        {
            return likedResult;
        }

        var likedSongs = likedResult.Value;
        if (likedSongs is null || likedSongs.Count == 0)
        {
            _logger.LogWarning("没有喜欢的歌曲，无法构建画像");
            return Result.Failure("没有喜欢的歌曲");
        }

        // 反序列化所有向量
        var acousticVectors = new List<float[]>();
        var deepVectors = new List<float[]>();

        foreach (var song in likedSongs)
        {
            if (song.AcousticVectorBlob is not null)
            {
                acousticVectors.Add(_vectorSerializer.Deserialize(song.AcousticVectorBlob));
            }
            if (song.DeepVectorBlob is not null)
            {
                deepVectors.Add(_vectorSerializer.Deserialize(song.DeepVectorBlob));
            }
        }

        if (acousticVectors.Count == 0)
        {
            _logger.LogWarning("没有歌曲包含声学特征向量");
            return Result.Failure("没有可用的特征向量");
        }

        // 计算均值向量
        var acousticMean = ComputeMean(acousticVectors);
        var deepMean = deepVectors.Count > 0 ? ComputeMean(deepVectors) : null;

        var userProfile = new UserProfile
        {
            Id = 1,
            AcousticMeanVector = acousticMean,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(acousticMean),
            DeepMeanVector = deepMean,
            DeepMeanVectorBlob = deepMean is not null ? _vectorSerializer.Serialize(deepMean) : null,
            LastUpdated = DateTime.UtcNow
        };

        return await _profileRepository.SaveAsync(userProfile);
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateProfileIncrementalAsync(int newLikedSongId)
    {
        var profileResult = await _profileRepository.GetAsync();
        if (!profileResult.IsSuccess)
        {
            return profileResult;
        }

        var songResult = await _songRepository.GetByIdAsync(newLikedSongId);
        if (!songResult.IsSuccess)
        {
            return songResult;
        }

        var song = songResult.Value!;
        var profile = profileResult.Value;

        // 如果没有画像，执行全量重建
        if (profile?.AcousticMeanVector is null)
        {
            return await RebuildProfileAsync();
        }

        // 使用 Welford 在线算法增量更新
        var currentAcoustic = profile.AcousticMeanVector;
        var likedResult = await _songRepository.GetLikedSongsAsync();
        var count = likedResult.IsSuccess && likedResult.Value is not null ? likedResult.Value.Count : 1;

        float[]? updatedAcoustic = null;
        if (song.AcousticVectorBlob is not null)
        {
            var newVector = _vectorSerializer.Deserialize(song.AcousticVectorBlob);
            updatedAcoustic = IncrementalMean(currentAcoustic, newVector, count);
        }

        float[]? updatedDeep = null;
        if (profile.DeepMeanVector is not null && song.DeepVectorBlob is not null)
        {
            var newDeepVector = _vectorSerializer.Deserialize(song.DeepVectorBlob);
            updatedDeep = IncrementalMean(profile.DeepMeanVector, newDeepVector, count);
        }

        var updatedProfile = new UserProfile
        {
            Id = 1,
            AcousticMeanVector = updatedAcoustic ?? currentAcoustic,
            AcousticMeanVectorBlob = updatedAcoustic is not null
                ? _vectorSerializer.Serialize(updatedAcoustic)
                : profile.AcousticMeanVectorBlob,
            DeepMeanVector = updatedDeep ?? profile.DeepMeanVector,
            DeepMeanVectorBlob = updatedDeep is not null
                ? _vectorSerializer.Serialize(updatedDeep)
                : profile.DeepMeanVectorBlob,
            LastUpdated = DateTime.UtcNow
        };

        return await _profileRepository.SaveAsync(updatedProfile);
    }

    /// <inheritdoc/>
    public async Task<bool> HasProfileAsync()
    {
        var result = await _profileRepository.GetAsync();
        return result.IsSuccess && result.Value?.AcousticMeanVectorBlob is not null;
    }

    /// <summary>计算向量均值</summary>
    private static float[] ComputeMean(IReadOnlyList<float[]> vectors)
    {
        var dimension = vectors[0].Length;
        var mean = new float[dimension];

        foreach (var vector in vectors)
        {
            for (var i = 0; i < dimension; i++)
            {
                mean[i] += vector[i];
            }
        }

        for (var i = 0; i < dimension; i++)
        {
            mean[i] /= vectors.Count;
        }

        return mean;
    }

    /// <summary>增量更新均值（Welford 算法）</summary>
    private static float[] IncrementalMean(float[] currentMean, float[] newVector, int currentCount)
    {
        var result = new float[currentMean.Length];
        var newCount = currentCount + 1;

        for (var i = 0; i < currentMean.Length; i++)
        {
            result[i] = currentMean[i] + (newVector[i] - currentMean[i]) / newCount;
        }

        return result;
    }
}
