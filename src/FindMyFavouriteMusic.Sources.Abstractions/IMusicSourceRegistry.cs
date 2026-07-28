namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>已注册音乐源注册表。</summary>
public interface IMusicSourceRegistry
{
    IReadOnlyList<IMusicSourcePlugin> GetAll();
    IMusicSourcePlugin? TryGet(string sourceId);
    IMusicSourcePlugin GetRequired(string sourceId);
}
