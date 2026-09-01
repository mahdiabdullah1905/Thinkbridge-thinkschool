namespace QuotesApi.Jobs;

/// <summary>
/// Drains <see cref="IBackgroundJobQueue"/> for the lifetime of the application.
/// One job runs at a time; each gets its own DI scope so it can use scoped
/// services (repositories, DbContext) exactly like a request would.
/// </summary>
public class QueuedJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueuedJobWorker> _logger;

    public QueuedJobWorker(IBackgroundJobQueue queue, IServiceProvider serviceProvider, ILogger<QueuedJobWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueuedJobWorker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> workItem;
            try
            {
                workItem = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Application is shutting down mid-job; stop the loop instead of
                // trying to pick up more work.
                break;
            }
            catch (Exception ex)
            {
                // A single job failing must never take the worker down - log it and
                // move on to the next item. Job-level failure state (if the caller
                // wants it) is the job's own responsibility to record.
                _logger.LogError(ex, "A background job threw an unhandled exception.");
            }
        }

        _logger.LogInformation("QueuedJobWorker stopping.");
    }
}
