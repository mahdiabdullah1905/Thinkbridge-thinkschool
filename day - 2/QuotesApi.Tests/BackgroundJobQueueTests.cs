using QuotesApi.Jobs;
using Xunit;

namespace QuotesApi.Tests;

public class BackgroundJobQueueTests
{
    [Fact]
    public async Task QueueAsync_ThenDequeueAsync_ReturnsTheQueuedWorkItem()
    {
        var queue = new BackgroundJobQueue();
        var ran = false;

        await queue.QueueAsync((_, _) => { ran = true; return Task.CompletedTask; });

        var workItem = await queue.DequeueAsync(CancellationToken.None);
        await workItem(null!, CancellationToken.None);

        Assert.True(ran);
    }

    [Fact]
    public async Task QueueAsync_MultipleItems_AreDequeuedInFifoOrder()
    {
        var queue = new BackgroundJobQueue();
        var order = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var captured = i;
            await queue.QueueAsync((_, _) => { order.Add(captured); return Task.CompletedTask; });
        }

        for (var i = 0; i < 5; i++)
        {
            var workItem = await queue.DequeueAsync(CancellationToken.None);
            await workItem(null!, CancellationToken.None);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, order);
    }

    [Fact]
    public async Task DequeueAsync_WhenCancelledBeforeAnyItemIsQueued_ThrowsOperationCanceledException()
    {
        var queue = new BackgroundJobQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queue.DequeueAsync(cts.Token));
    }
}
