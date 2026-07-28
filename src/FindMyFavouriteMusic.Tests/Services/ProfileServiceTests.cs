using FluentAssertions;
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
/// ProfileService 单元测试。
/// </summary>
/// <remarks>
/// ProfileRepository 的方法非 virtual，无法用 Moq 直接 mock。
/// 这里采用 SQLite 内存数据库（Mode=Memory&Cache=Shared）测试 ProfileRepository 的真实行为，
/// 既能验证 ProfileService 的业务逻辑（均值计算、Welford 增量更新、回退重建等），
/// 又能覆盖仓储的持久化交互，比 mock 具体类更可靠。
/// 每个测试使用独立的内存数据库名（带 GUID），避免并行测试间相互污染；
/// 通过保持一个 keep-alive 连接打开，确保数据库在整个测试期间存活。
/// </remarks>
public class ProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly Mock<ISongRepository> _songRepositoryMock;
    private readonly ProfileRepository _profileRepository;
    private readonly VectorSerializer _vectorSerializer;
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        // 使用带 Cache=Shared 的内存数据库：允许多个连接共享同一内存数据库
        // 保持 keep-alive 连接打开，防止数据库在所有连接关闭后被 GC 回收
        var dbName = $"profile_test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(connectionString);
        _keepAliveConnection.Open();

        // 建表（与 DatabaseInitializer 中的 UserProfile 表结构一致）
        InitializeSchema();

        var dbOptions = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
        _songRepositoryMock = new Mock<ISongRepository>();
        _profileRepository = new ProfileRepository(dbOptions, Mock.Of<ILogger<ProfileRepository>>());
        _vectorSerializer = new VectorSerializer();
        _service = new ProfileService(
            _songRepositoryMock.Object,
            _profileRepository,
            _vectorSerializer,
            dbOptions,
            Mock.Of<ILogger<ProfileService>>());
    }

    /// <summary>
    /// 创建 UserProfile 表，确保仓储可以执行查询与 upsert。
    /// </summary>
    private void InitializeSchema()
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS UserProfile (
                Id INTEGER PRIMARY KEY,
                AcousticMeanVector BLOB,
                DeepMeanVector BLOB,
                AcousticSampleCount INTEGER DEFAULT 0,
                DeepSampleCount INTEGER DEFAULT 0,
                LastUpdated DATETIME
            )
            """;
        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 没有喜欢的歌曲时，全量重建应清空画像并返回成功。
    /// </summary>
    [Fact]
    public async Task RebuildProfileAsync_NoLikedSongs_ClearsProfile()
    {
        // Arrange: 先写入画像，再清空喜欢列表
        await _profileRepository.SaveAsync(new UserProfile
        {
            Id = 1,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 1f, 2f }),
            LastUpdated = DateTime.UtcNow
        });
        _songRepositoryMock
            .Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(new List<Song>()));

        // Act
        var result = await _service.RebuildProfileAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var profileResult = await _profileRepository.GetAsync();
        profileResult.IsSuccess.Should().BeTrue();
        profileResult.Value!.AcousticMeanVectorBlob.Should().BeNull();
        profileResult.Value.DeepMeanVectorBlob.Should().BeNull();
    }

    /// <summary>
    /// 喜欢歌曲均无声学特征向量时，全量重建应清空画像并返回成功。
    /// </summary>
    [Fact]
    public async Task RebuildProfileAsync_NoAcousticVectors_ClearsProfile()
    {
        // Arrange: 喜欢列表中的歌曲均无 AcousticVectorBlob
        var songs = new List<Song>
        {
            new() { Id = 1, IsLiked = true, AcousticVectorBlob = null },
            new() { Id = 2, IsLiked = true, AcousticVectorBlob = null }
        };
        _songRepositoryMock
            .Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(songs));

        // Act
        var result = await _service.RebuildProfileAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var profileResult = await _profileRepository.GetAsync();
        profileResult.Value!.AcousticMeanVectorBlob.Should().BeNull();
    }

    /// <summary>
    /// 有效喜欢歌曲时，全量重建应计算各维度均值并持久化画像。
    /// </summary>
    [Fact]
    public async Task RebuildProfileAsync_ValidLikedSongs_ComputesMeanAndSaves()
    {
        // Arrange: 两首喜欢歌曲，声学向量分别为 [2,4] 与 [4,6]，均值应为 [3,5]
        var songs = new List<Song>
        {
            new() { Id = 1, IsLiked = true, AcousticVectorBlob = _vectorSerializer.Serialize(new float[] { 2f, 4f }) },
            new() { Id = 2, IsLiked = true, AcousticVectorBlob = _vectorSerializer.Serialize(new float[] { 4f, 6f }) }
        };
        _songRepositoryMock
            .Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(songs));

        // Act
        var result = await _service.RebuildProfileAsync();

        // Assert: 重建成功，且画像已持久化到数据库
        result.IsSuccess.Should().BeTrue();
        var profileResult = await _profileRepository.GetAsync();
        profileResult.IsSuccess.Should().BeTrue();
        profileResult.Value!.AcousticMeanVectorBlob.Should().NotBeNull();
        var savedMean = _vectorSerializer.Deserialize(profileResult.Value.AcousticMeanVectorBlob!);
        savedMean.Should().HaveCount(2);
        savedMean[0].Should().BeApproximately(3f, 0.0001f);
        savedMean[1].Should().BeApproximately(5f, 0.0001f);
        profileResult.Value.AcousticSampleCount.Should().Be(2);
        profileResult.Value.DeepSampleCount.Should().Be(0);
    }

    /// <summary>
    /// 增量更新时若画像不存在（BLOB 为 null），应回退到全量重建。
    /// </summary>
    [Fact]
    public async Task UpdateProfileIncrementalAsync_NoProfile_FallsBackToRebuild()
    {
        // Arrange: 数据库无画像；新歌曲有声学向量；喜欢列表包含该歌曲
        var newSong = new Song
        {
            Id = 5,
            IsLiked = true,
            AcousticVectorBlob = _vectorSerializer.Serialize(new float[] { 2f, 4f })
        };
        _songRepositoryMock.Setup(r => r.GetByIdAsync(5))
            .ReturnsAsync(Result<Song>.Success(newSong));
        _songRepositoryMock.Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(new List<Song> { newSong }));

        // Act: AcousticMeanVectorBlob 为 null，应触发回退到 RebuildProfileAsync
        var result = await _service.UpdateProfileIncrementalAsync(5);

        // Assert: 重建成功，画像已保存
        result.IsSuccess.Should().BeTrue();
        var profileResult = await _profileRepository.GetAsync();
        profileResult.IsSuccess.Should().BeTrue();
        profileResult.Value!.AcousticMeanVectorBlob.Should().NotBeNull();
    }

    /// <summary>
    /// 已有画像时增量更新应使用 Welford 公式更新均值：
    /// new_mean = old_mean + (new_vector - old_mean) / new_count。
    /// </summary>
    [Fact]
    public async Task UpdateProfileIncrementalAsync_WithExistingProfile_UpdatesMeanUsingWelford()
    {
        // Arrange: 已有画像基于 1 首歌曲，均值 = [4,6]
        var existingProfile = new UserProfile
        {
            Id = 1,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 4f, 6f }),
            AcousticSampleCount = 1,
            LastUpdated = DateTime.UtcNow
        };
        await _profileRepository.SaveAsync(existingProfile);

        // 新歌曲特征向量 = [2,4]
        var newSong = new Song
        {
            Id = 5,
            IsLiked = true,
            AcousticVectorBlob = _vectorSerializer.Serialize(new float[] { 2f, 4f })
        };
        _songRepositoryMock.Setup(r => r.GetByIdAsync(5))
            .ReturnsAsync(Result<Song>.Success(newSong));

        // Act
        var result = await _service.UpdateProfileIncrementalAsync(5);

        // Assert: Welford 公式（previousCount=1 → newCount=2）
        // new_mean[0] = 4 + (2 - 4) / 2 = 3
        // new_mean[1] = 6 + (4 - 6) / 2 = 5
        result.IsSuccess.Should().BeTrue();
        var profileResult = await _profileRepository.GetAsync();
        profileResult.IsSuccess.Should().BeTrue();
        profileResult.Value!.AcousticSampleCount.Should().Be(2);
        var savedMean = _vectorSerializer.Deserialize(profileResult.Value.AcousticMeanVectorBlob!);
        savedMean.Should().HaveCount(2);
        savedMean[0].Should().BeApproximately(3f, 0.0001f);
        savedMean[1].Should().BeApproximately(5f, 0.0001f);
    }

    /// <summary>
    /// 新喜欢歌曲无声学向量时：不更新声学均值，且 AcousticSampleCount 保持画像原值
    /// （不得用喜欢列表重算覆盖，否则会与均值实际样本数脱节）。
    /// </summary>
    [Fact]
    public async Task UpdateProfileIncrementalAsync_SongWithoutAcoustic_PreservesAcousticSampleCount()
    {
        var mean = new float[] { 4f, 6f };
        await _profileRepository.SaveAsync(new UserProfile
        {
            Id = 1,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(mean),
            DeepMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 1f }),
            AcousticSampleCount = 2,
            DeepSampleCount = 1,
            LastUpdated = DateTime.UtcNow
        });

        // 喜欢列表里另有带声学的歌；若误用「喜欢列表中有声学向量的数量」重算，
        // 可能得到 3（或其它与均值无关的数），与真实均值样本数 2 不一致。
        var songWithoutAcoustic = new Song
        {
            Id = 9,
            IsLiked = true,
            AcousticVectorBlob = null,
            DeepVectorBlob = null
        };
        _songRepositoryMock.Setup(r => r.GetByIdAsync(9))
            .ReturnsAsync(Result<Song>.Success(songWithoutAcoustic));
        _songRepositoryMock.Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(
            [
                new Song { Id = 1, AcousticVectorBlob = _vectorSerializer.Serialize([1f, 1f]) },
                new Song { Id = 2, AcousticVectorBlob = _vectorSerializer.Serialize([2f, 2f]) },
                new Song { Id = 3, AcousticVectorBlob = _vectorSerializer.Serialize([3f, 3f]) }, // 未参与当前均值
                songWithoutAcoustic
            ]));

        var result = await _service.UpdateProfileIncrementalAsync(9);

        result.IsSuccess.Should().BeTrue();
        var profile = (await _profileRepository.GetAsync()).Value!;
        profile.AcousticSampleCount.Should().Be(2);
        profile.DeepSampleCount.Should().Be(1);
        var savedMean = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob!);
        savedMean[0].Should().BeApproximately(4f, 0.0001f);
        savedMean[1].Should().BeApproximately(6f, 0.0001f);
    }

    /// <summary>
    /// 新歌仅含深度向量、无声学时：深度样本数递增，声学样本数与均值不变。
    /// </summary>
    [Fact]
    public async Task UpdateProfileIncrementalAsync_SongDeepOnly_UpdatesDeepCountKeepsAcoustic()
    {
        await _profileRepository.SaveAsync(new UserProfile
        {
            Id = 1,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 4f, 6f }),
            DeepMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 10f }),
            AcousticSampleCount = 2,
            DeepSampleCount = 1,
            LastUpdated = DateTime.UtcNow
        });

        var song = new Song
        {
            Id = 7,
            IsLiked = true,
            AcousticVectorBlob = null,
            DeepVectorBlob = _vectorSerializer.Serialize(new float[] { 0f })
        };
        _songRepositoryMock.Setup(r => r.GetByIdAsync(7))
            .ReturnsAsync(Result<Song>.Success(song));

        var result = await _service.UpdateProfileIncrementalAsync(7);

        result.IsSuccess.Should().BeTrue();
        var profile = (await _profileRepository.GetAsync()).Value!;
        profile.AcousticSampleCount.Should().Be(2);
        profile.DeepSampleCount.Should().Be(2);
        // deep: 10 + (0-10)/2 = 5
        var deepMean = _vectorSerializer.Deserialize(profile.DeepMeanVectorBlob!);
        deepMean[0].Should().BeApproximately(5f, 0.0001f);
        var acousticMean = _vectorSerializer.Deserialize(profile.AcousticMeanVectorBlob!);
        acousticMean[0].Should().BeApproximately(4f, 0.0001f);
    }

    /// <summary>
    /// 有画像与喜欢歌曲时，GetProfileAsync 应返回 DTO 统计。
    /// </summary>
    [Fact]
    public async Task GetProfileAsync_WithProfile_ReturnsDto()
    {
        await _profileRepository.SaveAsync(new UserProfile
        {
            AcousticMeanVectorBlob = _vectorSerializer.Serialize([1f]),
            DeepMeanVectorBlob = _vectorSerializer.Serialize([2f]),
            AcousticSampleCount = 1,
            DeepSampleCount = 1,
            LastUpdated = DateTime.UtcNow
        });
        _songRepositoryMock.Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(
            [
                new Song { Id = 1, FilePath = "/a.mp3", IsLiked = true }
            ]));

        var result = await _service.GetProfileAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value!.LikedSongCount.Should().Be(1);
        result.Value.HasDeepProfile.Should().BeTrue();
    }

    /// <summary>
    /// 仓储查询画像失败时（如表不存在），GetProfileAsync 应返回失败。
    /// </summary>
    [Fact]
    public async Task GetProfileAsync_RepositoryFails_ReturnsFailure()
    {
        // Arrange: 删除 UserProfile 表模拟仓储查询失败
        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = "DROP TABLE UserProfile";
        cmd.ExecuteNonQuery();

        // Act
        var result = await _service.GetProfileAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// 画像已存在时，HasProfileAsync 应返回 true。
    /// </summary>
    [Fact]
    public async Task HasProfileAsync_ProfileExists_ReturnsTrue()
    {
        // Arrange: 数据库已保存画像
        var profile = new UserProfile
        {
            Id = 1,
            AcousticMeanVectorBlob = _vectorSerializer.Serialize(new float[] { 1f, 2f }),
            LastUpdated = DateTime.UtcNow
        };
        await _profileRepository.SaveAsync(profile);

        // Act
        var result = await _service.HasProfileAsync();

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// 画像不存在时，HasProfileAsync 应返回 false。
    /// </summary>
    [Fact]
    public async Task HasProfileAsync_ProfileNotExists_ReturnsFalse()
    {
        // Arrange: 数据库为空表（构造函数已建表但未插入数据）

        // Act
        var result = await _service.HasProfileAsync();

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// 释放 keep-alive 连接并清理连接池，使内存数据库被回收。
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearPool(_keepAliveConnection);
        _keepAliveConnection.Dispose();
    }
}
