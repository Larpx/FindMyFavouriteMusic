namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

/// <summary>
/// 模型加载与扫描/预测之间的互斥门闩，避免切模型时并发推理触发 ObjectDisposedException。
/// </summary>
public interface IModelOperationLock
{
    /// <summary>
    /// 异步获取互斥锁；释放返回的对象以解锁。
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default);

    /// <summary>
    /// 同步获取互斥锁（供同步的 LoadModel 使用）。
    /// </summary>
    IDisposable Acquire();
}
