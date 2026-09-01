namespace QuotesApi.Jobs;

/// <summary>
/// A thread-safe hand-off point between request threads (producers) and the
/// single background worker (consumer). Each work item receives a scoped
/// IServiceProvider so it can resolve scoped services (e.g. repositories)
/// the same way a controller/endpoint would.
/// </summary>
public interface IBackgroundJobQueue
{
    ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem, CancellationToken ct = default);

    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}
