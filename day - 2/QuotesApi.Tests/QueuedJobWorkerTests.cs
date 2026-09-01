using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Jobs;
using Xunit;

namespace QuotesApi.Tests;

public class QueuedJobWorkerTests : IDisposable
{
    private readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private QueuedJobWorker CreateWorker(BackgroundJobQueue queue) =>
        new(queue, _emptyServiceProvider, NullLogger<QueuedJobWorker>.Instance);

    [Fact]
    public async Task Worker_ProcessesAQueuedJob()
    {
        var queue = new BackgroundJobQueue();
        var worker = CreateWorker(queue);
        var jobRan = new TaskCompletionSource();

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueAsync((_, _) => { jobRan.SetResult(); return Task.CompletedTask; });

            var completed = await Task.WhenAny(jobRan.Task, Task.Delay(WaitTimeout));
            Assert.Same(jobRan.Task, completed);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_ProcessesMultipleQueuedJobs_AllOfThem()
    {
        var queue = new BackgroundJobQueue();
        var worker = CreateWorker(queue);
        const int jobCount = 5;
        var processed = new List<int>();
        var allDone = new TaskCompletionSource();

        await worker.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < jobCount; i++)
            {
                var captured = i;
                await queue.QueueAsync((_, _) =>
                {
                    lock (processed)
                    {
                        processed.Add(captured);
                        if (processed.Count == jobCount) allDone.TrySetResult();
                    }
                    return Task.CompletedTask;
                });
            }

            var completed = await Task.WhenAny(allDone.Task, Task.Delay(WaitTimeout));
            Assert.Same(allDone.Task, completed);
            Assert.Equal(jobCount, processed.Count);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_WhenAJobThrows_KeepsRunningAndProcessesTheNextJob()
    {
        var queue = new BackgroundJobQueue();
        var worker = CreateWorker(queue);
        var secondJobRan = new TaskCompletionSource();

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueAsync((_, _) => throw new InvalidOperationException("simulated job failure"));
            await queue.QueueAsync((_, _) => { secondJobRan.SetResult(); return Task.CompletedTask; });

            var completed = await Task.WhenAny(secondJobRan.Task, Task.Delay(WaitTimeout));
            Assert.Same(secondJobRan.Task, completed);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_OnShutdown_CancelsInFlightWorkAndStopsCleanly()
    {
        var queue = new BackgroundJobQueue();
        var worker = CreateWorker(queue);
        var jobStarted = new TaskCompletionSource();
        var jobObservedCancellation = new TaskCompletionSource();

        await worker.StartAsync(CancellationToken.None);

        await queue.QueueAsync(async (_, ct) =>
        {
            jobStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                jobObservedCancellation.SetResult();
                throw;
            }
        });

        var started = await Task.WhenAny(jobStarted.Task, Task.Delay(WaitTimeout));
        Assert.Same(jobStarted.Task, started);

        // BackgroundService.StopAsync cancels the stoppingToken passed to ExecuteAsync
        // and awaits the worker's loop; this must return promptly, not hang.
        var stopTask = worker.StopAsync(CancellationToken.None);
        var stopped = await Task.WhenAny(stopTask, Task.Delay(WaitTimeout));
        Assert.Same(stopTask, stopped);
        await stopTask;

        Assert.True(jobObservedCancellation.Task.IsCompletedSuccessfully);
    }

    public void Dispose()
    {
        (_emptyServiceProvider as IDisposable)?.Dispose();
    }
}
