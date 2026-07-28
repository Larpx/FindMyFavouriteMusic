using FluentAssertions;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Tests.Core;

/// <summary>
/// <see cref="ModelOperationLock"/> 互斥行为测试。
/// </summary>
public class ModelOperationLockTests
{
    [Fact]
    public async Task AcquireAsync_SerializesConcurrentHolders()
    {
        using var gate = new ModelOperationLock();
        var order = new List<int>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await using var h = await gate.AcquireAsync();
            order.Add(1);
            started.SetResult();
            await Task.Delay(80);
            order.Add(2);
        });

        await started.Task;
        var second = Task.Run(async () =>
        {
            await using var h = await gate.AcquireAsync();
            order.Add(3);
        });

        await Task.WhenAll(first, second);
        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Acquire_Sync_ReleasesOnDispose()
    {
        using var gate = new ModelOperationLock();
        using (gate.Acquire())
        {
            // held
        }

        using var second = gate.Acquire();
        second.Should().NotBeNull();
    }
}
