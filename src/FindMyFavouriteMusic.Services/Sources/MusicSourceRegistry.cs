using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;

/// <summary>进程内音乐源注册表。</summary>
public sealed class MusicSourceRegistry : IMusicSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IMusicSourcePlugin> _map;

    public MusicSourceRegistry(IEnumerable<IMusicSourcePlugin> plugins)
    {
        _map = plugins.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IMusicSourcePlugin> GetAll() => _map.Values.ToList();

    public IMusicSourcePlugin? TryGet(string sourceId) =>
        _map.TryGetValue(sourceId, out var p) ? p : null;

    public IMusicSourcePlugin GetRequired(string sourceId) =>
        TryGet(sourceId) ?? throw new KeyNotFoundException($"未注册音乐源: {sourceId}");
}
