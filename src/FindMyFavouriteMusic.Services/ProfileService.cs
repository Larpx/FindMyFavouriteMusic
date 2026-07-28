using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 用户画像服务，负责画像构建（全量重建 / 增量更新）。
/// </summary>
/// <remarks>
/// 画像是用户"喜欢"歌曲特征向量的均值，代表了用户的音乐品味中心。
/// <para>构建策略：</para>
/// <para>1. 全量重建（<see cref="RebuildProfileAsync"/>）：遍历所有喜欢歌曲，求各维度均值；</para>
/// <para>2. 增量更新（<see cref="UpdateProfileIncrementalAsync"/>）：使用 Welford 在线算法，O(1) 更新均值。</para>
/// <para>触发时机：标记喜欢 → 增量更新；取消喜欢 → 全量重建（增量更新无法"减去"一首歌）。</para>
/// </remarks>
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
        // dbOptions 注入保证配置链完整（未来可用于多画像库切换）
        _ = dbOptions;
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

    /// <summary>
    /// 全量重建画像：遍历所有标记喜欢的歌曲，计算特征向量各维度的均值。
    /// </summary>
    /// <remarks>
    /// 算法：mean[i] = Σ vector[i] / N，其中 N 为喜欢歌曲数。
    /// <para>同时计算声学均值与深度均值（若存在深度特征）。</para>
    /// </remarks>
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
            // 取消全部喜欢后必须清空画像，避免继续用旧均值做预测
            _logger.LogInformation("没有喜欢的歌曲，清空用户画像");
            return await ClearProfileAsync();
        }

        // 反序列化所有喜欢歌曲的特征向量
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
            _logger.LogWarning("没有歌曲包含声学特征向量，清空画像");
            return await ClearProfileAsync();
        }

        // 计算各维度均值；声学/深度样本数分别统计（深度子集可能更少）
        var acousticMean = ComputeMean(acousticVectors);
        var deepMean = deepVectors.Count > 0 ? ComputeMean(deepVectors) : null;

        var userProfile = new UserProfile
        {
            Id = 1,
            AcousticMeanVector = acousticMean,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(acousticMean),
            DeepMeanVector = deepMean,
            DeepMeanVectorBlob = deepMean is not null ? _vectorSerializer.Serialize(deepMean) : null,
            AcousticSampleCount = acousticVectors.Count,
            DeepSampleCount = deepVectors.Count,
            LastUpdated = DateTime.UtcNow
        };

        _logger.LogInformation(
            "画像重建完成：声学样本 {AcousticCount}，深度样本 {DeepCount}",
            acousticVectors.Count, deepVectors.Count);

        return await _profileRepository.SaveAsync(userProfile);
    }

    /// <summary>清空画像向量（保留表行，BLOB 置空）。</summary>
    private Task<Result> ClearProfileAsync()
    {
        var empty = new UserProfile
        {
            Id = 1,
            AcousticMeanVector = null,
            AcousticMeanVectorBlob = null,
            DeepMeanVector = null,
            DeepMeanVectorBlob = null,
            AcousticSampleCount = 0,
            DeepSampleCount = 0,
            LastUpdated = DateTime.UtcNow
        };
        return _profileRepository.SaveAsync(empty);
    }

    /// <summary>
    /// 增量更新画像：使用 Welford 在线算法，O(1) 时间更新均值向量。
    /// </summary>
    /// <remarks>
    /// Welford 公式：new_mean = old_mean + (new_vector - old_mean) / new_count
    /// <para>相比全量重建（O(N)），增量更新只需 O(1)，适合频繁标记场景。</para>
    /// <para>注意：仓储仅持久化 BLOB，因此需先反序列化 BLOB 得到 float[] 再参与计算。</para>
    /// </remarks>
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

        // 若画像不存在（BLOB 为空），回退到全量重建
        if (profile?.AcousticMeanVectorBlob is null)
        {
            return await RebuildProfileAsync();
        }

        // 从 BLOB 反序列化得到当前均值向量（仓储不填充 float[] 字段）
        var currentAcoustic = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob);
        float[]? currentDeep = profile.DeepMeanVectorBlob is not null
            ? _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob)
            : null;

        // 分别统计声学/深度样本数：喜欢列表已含新歌，增量前样本数 = 对应向量数 - 1（若新歌有该向量）
        var likedResult = await _songRepository.GetLikedSongsAsync();
        if (!likedResult.IsSuccess || likedResult.Value is null)
        {
            return await RebuildProfileAsync();
        }

        var likedSongs = likedResult.Value;
        var acousticSampleCount = likedSongs.Count(s => s.AcousticVectorBlob is not null);
        var deepSampleCount = likedSongs.Count(s => s.DeepVectorBlob is not null);
        var previousAcousticCount = song.AcousticVectorBlob is not null
            ? Math.Max(1, acousticSampleCount - 1)
            : Math.Max(1, acousticSampleCount);
        var previousDeepCount = song.DeepVectorBlob is not null
            ? Math.Max(1, deepSampleCount - 1)
            : Math.Max(1, deepSampleCount);

        float[]? updatedAcoustic = null;
        if (song.AcousticVectorBlob is not null)
        {
            var newVector = _vectorSerializer.Deserialize(song.AcousticVectorBlob);
            updatedAcoustic = IncrementalMean(currentAcoustic, newVector, previousAcousticCount);
        }

        // 深度均值更新：需处理画像尚未建立深度均值的过渡场景
        float[]? updatedDeep = null;
        if (currentDeep is not null && song.DeepVectorBlob is not null)
        {
            var newDeepVector = _vectorSerializer.Deserialize(song.DeepVectorBlob);
            updatedDeep = IncrementalMean(currentDeep, newDeepVector, previousDeepCount);
        }
        else if (currentDeep is null && song.DeepVectorBlob is not null)
        {
            _logger.LogInformation("画像尚无深度均值，新歌曲包含深度向量，触发全量重建以建立深度均值");
            return await RebuildProfileAsync();
        }

        var updatedProfile = new UserProfile
        {
            Id = 1,
            AcousticMeanVector = updatedAcoustic ?? currentAcoustic,
            AcousticMeanVectorBlob = updatedAcoustic is not null
                ? _vectorSerializer.Serialize(updatedAcoustic)
                : profile.AcousticMeanVectorBlob,
            DeepMeanVector = updatedDeep ?? currentDeep,
            DeepMeanVectorBlob = updatedDeep is not null
                ? _vectorSerializer.Serialize(updatedDeep)
                : profile.DeepMeanVectorBlob,
            AcousticSampleCount = acousticSampleCount,
            DeepSampleCount = updatedDeep is not null || currentDeep is not null ? deepSampleCount : 0,
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

    /// <summary>
    /// 计算向量集合的各维度均值。
    /// <para>mean[i] = Σ vector_k[i] / N，k=1..N</para>
    /// </summary>
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

    /// <summary>
    /// Welford 在线均值更新算法。
    /// <para>公式：new_mean = old_mean + (new_value - old_mean) / new_count</para>
    /// <para>优势：无需保存历史所有样本，仅 O(1) 时间与 O(d) 空间。</para>
    /// </summary>
    /// <param name="currentMean">当前均值向量</param>
    /// <param name="newVector">新加入的样本向量</param>
    /// <param name="currentCount">加入前的样本数（即旧均值基于的样本数）</param>
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
