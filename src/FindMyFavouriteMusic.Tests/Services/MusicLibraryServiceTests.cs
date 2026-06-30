using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// MusicLibraryService 单元测试，覆盖目录扫描、歌曲入库、喜欢切换与查询等核心流程。
/// </summary>
/// <remarks>
/// 所有依赖均为接口，使用 Moq 进行隔离。
/// 涉及文件系统访问的扫描测试使用临时目录，测试结束后自动清理。
/// </remarks>
public class MusicLibraryServiceTests
{
    private readonly Mock<IAudioDecoder> _audioDecoderMock;
    private readonly Mock<IAcousticFeatureExtractor> _acousticExtractorMock;
    private readonly Mock<IDeepFeatureExtractor> _deepExtractorMock;
    private readonly Mock<IVectorSerializer> _vectorSerializerMock;
    private readonly Mock<ISongRepository> _songRepositoryMock;
    private readonly Mock<IProfileService> _profileServiceMock;
    private readonly MusicLibraryService _service;

    public MusicLibraryServiceTests()
    {
        _audioDecoderMock = new Mock<IAudioDecoder>();
        _acousticExtractorMock = new Mock<IAcousticFeatureExtractor>();
        _deepExtractorMock = new Mock<IDeepFeatureExtractor>();
        _vectorSerializerMock = new Mock<IVectorSerializer>();
        _songRepositoryMock = new Mock<ISongRepository>();
        _profileServiceMock = new Mock<IProfileService>();

        var featureOptions = Options.Create(new FeatureExtractionOptions { TargetSampleRate = 16000 });
        var scanOptions = Options.Create(new ScanOptions
        {
            SupportedExtensions = [".mp3", ".wav"],
            MaxConcurrentProcessing = 2
        });

        _service = new MusicLibraryService(
            _audioDecoderMock.Object,
            _acousticExtractorMock.Object,
            _deepExtractorMock.Object,
            _vectorSerializerMock.Object,
            _songRepositoryMock.Object,
            _profileServiceMock.Object,
            featureOptions,
            scanOptions,
            Mock.Of<ILogger<MusicLibraryService>>());
    }

    /// <summary>
    /// 扫描不存在的目录时应返回失败。
    /// </summary>
    [Fact]
    public async Task ScanDirectoryAsync_DirectoryNotExists_ReturnsFailure()
    {
        // Arrange: 构造一个不存在的目录路径
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        // Act
        var result = await _service.ScanDirectoryAsync(path);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// 目录中无音频文件时应返回空列表（成功）。
    /// </summary>
    [Fact]
    public async Task ScanDirectoryAsync_NoAudioFiles_ReturnsEmptyList()
    {
        // Arrange: 创建临时目录，仅放置一个非音频文件
        var tempDir = Path.Combine(Path.GetTempPath(), $"scan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "hello");

        try
        {
            // Act
            var result = await _service.ScanDirectoryAsync(tempDir);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// 有效目录下应处理所有音频文件（解码失败不阻断流程，仍入库基础信息）。
    /// </summary>
    [Fact]
    public async Task ScanDirectoryAsync_ValidDirectory_ProcessesAllFiles()
    {
        // Arrange: 创建临时目录与两个空音频文件
        var tempDir = Path.Combine(Path.GetTempPath(), $"scan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllBytes(Path.Combine(tempDir, "song1.mp3"), []);
        File.WriteAllBytes(Path.Combine(tempDir, "song2.wav"), []);

        // mock: 新歌曲（仓储返回 null），解码失败（空文件无法解码），插入返回成功
        _songRepositoryMock.Setup(r => r.GetByFilePathAsync(It.IsAny<string>()))
            .ReturnsAsync(Result<Song?>.Success(null));
        _songRepositoryMock.Setup(r => r.InsertAsync(It.IsAny<Song>()))
            .ReturnsAsync(Result<int>.Success(1));
        _audioDecoderMock.Setup(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Failure("解码失败"));

        try
        {
            // Act
            var result = await _service.ScanDirectoryAsync(tempDir);

            // Assert: 扫描成功，两个文件均触发入库
            result.IsSuccess.Should().BeTrue();
            _songRepositoryMock.Verify(r => r.InsertAsync(It.IsAny<Song>()), Times.Exactly(2));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// 标记喜欢时应触发画像增量更新，不应触发全量重建。
    /// </summary>
    [Fact]
    public async Task ToggleLikeAsync_Like_TriggersIncrementalUpdate()
    {
        // Arrange: 仓储更新成功；画像增量更新返回成功
        _songRepositoryMock.Setup(r => r.UpdateLikeStatusAsync(It.IsAny<int>(), true))
            .ReturnsAsync(Result.Success());
        _profileServiceMock.Setup(p => p.UpdateProfileIncrementalAsync(It.IsAny<int>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ToggleLikeAsync(1, isLiked: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _profileServiceMock.Verify(p => p.UpdateProfileIncrementalAsync(1), Times.Once);
        _profileServiceMock.Verify(p => p.RebuildProfileAsync(), Times.Never);
    }

    /// <summary>
    /// 取消喜欢时应触发全量重建，不应触发增量更新。
    /// </summary>
    [Fact]
    public async Task ToggleLikeAsync_Unlike_TriggersRebuild()
    {
        // Arrange: 仓储更新成功；画像全量重建返回成功
        _songRepositoryMock.Setup(r => r.UpdateLikeStatusAsync(It.IsAny<int>(), false))
            .ReturnsAsync(Result.Success());
        _profileServiceMock.Setup(p => p.RebuildProfileAsync())
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _service.ToggleLikeAsync(1, isLiked: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _profileServiceMock.Verify(p => p.RebuildProfileAsync(), Times.Once);
        _profileServiceMock.Verify(p => p.UpdateProfileIncrementalAsync(It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// 仓储更新喜欢状态失败时，应返回失败且不触发画像更新。
    /// </summary>
    [Fact]
    public async Task ToggleLikeAsync_RepositoryFails_ReturnsFailure()
    {
        // Arrange: 仓储更新失败
        _songRepositoryMock.Setup(r => r.UpdateLikeStatusAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(Result.Failure("更新失败"));

        // Act
        var result = await _service.ToggleLikeAsync(1, isLiked: true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        _profileServiceMock.Verify(p => p.UpdateProfileIncrementalAsync(It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// 歌曲已入库时应直接返回缓存，不再解码与特征提取。
    /// </summary>
    [Fact]
    public async Task ProcessSongAsync_ExistingSong_ReturnsCached()
    {
        // Arrange: 仓储返回已存在的歌曲
        var existingSong = new Song { Id = 10, FilePath = "/path/song.mp3", Title = "existing" };
        _songRepositoryMock.Setup(r => r.GetByFilePathAsync("/path/song.mp3"))
            .ReturnsAsync(Result<Song?>.Success(existingSong));

        // Act
        var result = await _service.ProcessSongAsync("/path/song.mp3");

        // Assert: 返回缓存数据，未触发解码
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(10);
        _audioDecoderMock.Verify(
            d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 新歌曲应触发解码、特征提取与入库的完整流程。
    /// </summary>
    [Fact]
    public async Task ProcessSongAsync_NewSong_DecodesAndExtracts()
    {
        // Arrange: 仓储无缓存，解码与声学特征提取均成功
        _songRepositoryMock.Setup(r => r.GetByFilePathAsync(It.IsAny<string>()))
            .ReturnsAsync(Result<Song?>.Success(null));
        _audioDecoderMock.Setup(d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<float[]>.Success(new float[] { 0.1f, 0.2f }));
        _acousticExtractorMock.Setup(e => e.Extract(It.IsAny<float[]>(), It.IsAny<int>()))
            .Returns(Result<float[]>.Success(new float[] { 1f, 2f }));
        _vectorSerializerMock.Setup(v => v.Serialize(It.IsAny<float[]>()))
            .Returns(new byte[] { 1, 2, 3, 4 });
        _songRepositoryMock.Setup(r => r.InsertAsync(It.IsAny<Song>()))
            .ReturnsAsync(Result<int>.Success(42));

        // Act
        var result = await _service.ProcessSongAsync("/path/new.mp3");

        // Assert: 调用了解码、特征提取与插入
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(42);
        _audioDecoderMock.Verify(
            d => d.DecodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _acousticExtractorMock.Verify(e => e.Extract(It.IsAny<float[]>(), It.IsAny<int>()), Times.Once);
        _songRepositoryMock.Verify(r => r.InsertAsync(It.IsAny<Song>()), Times.Once);
    }

    /// <summary>
    /// GetAllSongsAsync 应将实体列表转换为 DTO 列表，正确映射 HasFeatures 字段。
    /// </summary>
    [Fact]
    public async Task GetAllSongsAsync_ReturnsDtos()
    {
        // Arrange: 仓储返回两首歌曲，一首有特征一首无特征
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "/a.mp3", Title = "A", IsLiked = true, AcousticVectorBlob = new byte[] { 1 } },
            new() { Id = 2, FilePath = "/b.mp3", Title = "B", IsLiked = false, AcousticVectorBlob = null }
        };
        _songRepositoryMock.Setup(r => r.GetAllSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(songs));

        // Act
        var result = await _service.GetAllSongsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].HasFeatures.Should().BeTrue();
        result.Value![1].HasFeatures.Should().BeFalse();
    }

    /// <summary>
    /// GetLikedSongsAsync 应返回喜欢歌曲的 DTO 列表。
    /// </summary>
    [Fact]
    public async Task GetLikedSongsAsync_ReturnsDtos()
    {
        // Arrange: 仓储返回一首喜欢的歌曲
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "/a.mp3", Title = "A", IsLiked = true }
        };
        _songRepositoryMock.Setup(r => r.GetLikedSongsAsync())
            .ReturnsAsync(Result<IReadOnlyList<Song>>.Success(songs));

        // Act
        var result = await _service.GetLikedSongsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].IsLiked.Should().BeTrue();
    }
}
