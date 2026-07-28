namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// 基于 <see cref="SemaphoreSlim"/> 的模型操作互斥锁（非可重入）。
/// </summary>
public sealed class ModelOperationLock : IModelOperationLock, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new AsyncReleaser(_semaphore);
    }

    /// <inheritdoc/>
    public IDisposable Acquire()
    {
        _semaphore.Wait();
        return new SyncReleaser(_semaphore);
    }

    /// <inheritdoc/>
    public void Dispose() => _semaphore.Dispose();

    private sealed class AsyncReleaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class SyncReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
