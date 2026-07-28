namespace Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

/// <summary>已解析到本地可解码路径的音频。</summary>
public sealed class ResolvedAudio : IAsyncDisposable
{
    public required string FilePath { get; init; }
    public bool IsTemporary { get; init; }
    public string? Format { get; init; }
    public int? Bitrate { get; init; }

    private Func<ValueTask>? _cleanup;

    public ResolvedAudio WithCleanup(Func<ValueTask> cleanup)
    {
        _cleanup = cleanup;
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cleanup is not null)
        {
            await _cleanup();
            _cleanup = null;
        }

        if (IsTemporary && File.Exists(FilePath))
        {
            try { File.Delete(FilePath); }
            catch { /* best-effort */ }
        }
    }
}
