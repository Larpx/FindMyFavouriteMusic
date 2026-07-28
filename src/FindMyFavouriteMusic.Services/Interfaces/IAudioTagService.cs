using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 音频文件标签读写（TagLib），封面嵌入原文件，不入库。
/// </summary>
public interface IAudioTagService
{
    /// <summary>从原文件实时读取标签与封面。</summary>
    Result<SongDetailDto> ReadTags(string filePath);

    /// <summary>将标签/封面直接写回原文件。</summary>
    Result WriteTags(string filePath, SongMetadataUpdateDto update);
}
