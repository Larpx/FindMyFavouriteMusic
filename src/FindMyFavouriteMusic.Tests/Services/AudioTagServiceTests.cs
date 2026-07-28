using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Services;

/// <summary>
/// <see cref="AudioTagService"/> 读写标签与封面测试（真实 WAV + TagLib）。
/// </summary>
public class AudioTagServiceTests
{
    private readonly AudioTagService _service = new(Mock.Of<ILogger<AudioTagService>>());

    [Fact]
    public void ReadTags_MissingFile_ReturnsFailure()
    {
        var result = _service.ReadTags(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.wav"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void WriteAndReadTags_RoundTrip_PreservesFields()
    {
        var path = WavTestFile.CreateSilentWav();
        try
        {
            var write = _service.WriteTags(path, new SongMetadataUpdateDto
            {
                Title = "T1",
                Artist = "A1",
                Album = "Alb",
                AlbumArtist = "AA",
                Genre = "Pop",
                Year = 2024,
                Track = "3/12",
                Disc = "1/2",
                Comment = "cmt",
                Lyrics = "la la"
            });
            write.IsSuccess.Should().BeTrue();

            var read = _service.ReadTags(path);
            read.IsSuccess.Should().BeTrue();
            read.Value!.Title.Should().Be("T1");
            read.Value.Artist.Should().Be("A1");
            read.Value.Album.Should().Be("Alb");
            read.Value.AlbumArtist.Should().Be("AA");
            read.Value.Genre.Should().Be("Pop");
            read.Value.Year.Should().Be(2024);
            read.Value.Track.Should().Be("3/12");
            read.Value.Disc.Should().Be("1/2");
            read.Value.Comment.Should().Be("cmt");
            read.Value.Lyrics.Should().Be("la la");
            read.Value.IsReadOnlyFile.Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteTags_ReplaceAndClearCover_Works()
    {
        var path = WavTestFile.CreateSilentWav();
        try
        {
            // 1x1 PNG
            byte[] png =
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59, 0xE7, 0x00, 0x00,
                0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
            ];

            var setCover = _service.WriteTags(path, new SongMetadataUpdateDto
            {
                Title = "cover",
                ReplaceCover = true,
                CoverImageData = png,
                CoverMimeType = "image/png"
            });
            setCover.IsSuccess.Should().BeTrue();

            var withCover = _service.ReadTags(path);
            withCover.Value!.CoverImageData.Should().NotBeNullOrEmpty();

            var clear = _service.WriteTags(path, new SongMetadataUpdateDto
            {
                Title = "cover",
                ClearCover = true
            });
            clear.IsSuccess.Should().BeTrue();

            var noCover = _service.ReadTags(path);
            noCover.Value!.CoverImageData.Should().BeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteTags_ReadOnlyFile_ReturnsFailure()
    {
        var path = WavTestFile.CreateSilentWav();
        try
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            var result = _service.WriteTags(path, new SongMetadataUpdateDto { Title = "x" });
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("只读");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
