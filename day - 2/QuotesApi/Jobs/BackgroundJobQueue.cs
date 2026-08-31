using System.Threading.Channels;

namespace QuotesApi.Jobs;

public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    public BackgroundJobQueue(int capacity = 100)
    {
        // Bounded + Wait: a producer that queues faster than the single worker can
        // drain will await space instead of piling up unbounded work in memory.
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };

        _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(options);
    }

    public async ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _channel.Writer.WriteAsync(workItem, ct);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}
