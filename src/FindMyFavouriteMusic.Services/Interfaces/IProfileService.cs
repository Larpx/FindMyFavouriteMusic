using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Models.Results;

namespace FindMyFavouriteMusic.Services.Interfaces;

/// <summary>
/// 用户画像服务接口
/// </summary>
public interface IProfileService
{
    Task<Result<ProfileDto>> GetProfileAsync();
    Task<Result> RebuildProfileAsync();
    Task<Result> UpdateProfileIncrementalAsync(int newLikedSongId);
    Task<bool> HasProfileAsync();
}
