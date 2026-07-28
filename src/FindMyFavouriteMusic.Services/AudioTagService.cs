using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using TagLib;
using TagFile = TagLib.File;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services;

/// <summary>
/// 基于 TagLibSharp 的音频标签读写；封面写入 embedded picture，不写数据库。
/// </summary>
public class AudioTagService : IAudioTagService
{
    private readonly ILogger<AudioTagService> _logger;

    /// <summary>构造标签服务。</summary>
    public AudioTagService(ILogger<AudioTagService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Result<SongDetailDto> ReadTags(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return Result<SongDetailDto>.Failure($"文件不存在: {filePath}");
        }

        try
        {
            using var file = TagFile.Create(filePath);
            var tag = file.Tag;
            IPicture? cover = tag.Pictures.FirstOrDefault(p =>
                p.Type is PictureType.FrontCover or PictureType.Other)
                ?? tag.Pictures.FirstOrDefault();

            var detail = new SongDetailDto
            {
                FilePath = filePath,
                Title = NullIfEmpty(tag.Title),
                Artist = NullIfEmpty(tag.FirstPerformer),
                Album = NullIfEmpty(tag.Album),
                AlbumArtist = NullIfEmpty(tag.FirstAlbumArtist),
                Genre = NullIfEmpty(tag.FirstGenre),
                Year = tag.Year > 0 ? (int)tag.Year : null,
                Track = FormatTrack(tag.Track, tag.TrackCount),
                Disc = FormatTrack(tag.Disc, tag.DiscCount),
                Comment = NullIfEmpty(tag.Comment),
                Lyrics = NullIfEmpty(tag.Lyrics),
                CoverImageData = cover?.Data.Data,
                CoverMimeType = cover?.MimeType,
                DurationMs = (int)file.Properties.Duration.TotalMilliseconds,
                IsReadOnlyFile = IsReadOnly(filePath)
            };

            return Result<SongDetailDto>.Success(detail);
        }
        catch (UnsupportedFormatException ex)
        {
            _logger.LogWarning(ex, "不支持的标签格式: {FilePath}", filePath);
            return Result<SongDetailDto>.Failure($"不支持读写该文件的标签: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取标签失败: {FilePath}", filePath);
            return Result<SongDetailDto>.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public Result WriteTags(string filePath, SongMetadataUpdateDto update)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return Result.Failure($"文件不存在: {filePath}");
        }

        if (IsReadOnly(filePath))
        {
            return Result.Failure("文件为只读，无法保存标签");
        }

        try
        {
            using var file = TagFile.Create(filePath);
            var tag = file.Tag;

            tag.Title = update.Title ?? string.Empty;
            tag.Performers = string.IsNullOrWhiteSpace(update.Artist)
                ? []
                : [update.Artist];
            tag.Album = update.Album ?? string.Empty;
            tag.AlbumArtists = string.IsNullOrWhiteSpace(update.AlbumArtist)
                ? []
                : [update.AlbumArtist];
            tag.Genres = string.IsNullOrWhiteSpace(update.Genre)
                ? []
                : [update.Genre];
            tag.Year = update.Year is > 0 ? (uint)update.Year.Value : 0u;
            ApplyTrack(tag, update.Track, isDisc: false);
            ApplyTrack(tag, update.Disc, isDisc: true);
            tag.Comment = update.Comment ?? string.Empty;
            tag.Lyrics = update.Lyrics ?? string.Empty;

            if (update.ClearCover)
            {
                tag.Pictures = [];
            }
            else if (update.ReplaceCover && update.CoverImageData is { Length: > 0 })
            {
                var mime = string.IsNullOrWhiteSpace(update.CoverMimeType)
                    ? GuessMime(update.CoverImageData)
                    : update.CoverMimeType!;
                tag.Pictures =
                [
                    new Picture(new ByteVector(update.CoverImageData))
                    {
                        Type = PictureType.FrontCover,
                        MimeType = mime,
                        Description = "Cover"
                    }
                ];
            }

            file.Save();
            return Result.Success();
        }
        catch (UnsupportedFormatException ex)
        {
            _logger.LogWarning(ex, "写标签不支持的格式: {FilePath}", filePath);
            return Result.Failure($"该格式不支持写回标签: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写回标签失败: {FilePath}", filePath);
            return Result.Failure(ex);
        }
    }

    private static bool IsReadOnly(string filePath)
    {
        var attrs = System.IO.File.GetAttributes(filePath);
        return attrs.HasFlag(System.IO.FileAttributes.ReadOnly);
    }

    /// <summary>空白字符串归一为 null，便于 UI 空态展示。</summary>
    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>将 Track/Disc 的 number/count 格式化为 <c>n</c> 或 <c>n/m</c>。</summary>
    private static string? FormatTrack(uint number, uint count)
    {
        if (number == 0)
        {
            return null;
        }

        return count > 0 ? $"{number}/{count}" : number.ToString();
    }

    /// <summary>解析 UI 输入的曲目/碟号字符串并写回 TagLib 字段。</summary>
    private static void ApplyTrack(Tag tag, string? value, bool isDisc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (isDisc)
            {
                tag.Disc = 0;
                tag.DiscCount = 0;
            }
            else
            {
                tag.Track = 0;
                tag.TrackCount = 0;
            }

            return;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        _ = uint.TryParse(parts[0], out var num);
        uint count = 0;
        if (parts.Length > 1)
        {
            _ = uint.TryParse(parts[1], out count);
        }

        if (isDisc)
        {
            tag.Disc = num;
            tag.DiscCount = count;
        }
        else
        {
            tag.Track = num;
            tag.TrackCount = count;
        }
    }

    /// <summary>根据文件头猜测封面 MIME（缺省 jpeg）。</summary>
    private static string GuessMime(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50)
        {
            return "image/png";
        }

        return "image/jpeg";
    }
}
