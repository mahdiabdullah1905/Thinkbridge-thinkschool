using System.Text;
using QuotesApi.Repositories;

namespace QuotesApi.Jobs;

/// <summary>
/// Builds a text report of a collection's quotes. Registered as scoped because
/// it depends on the scoped repositories - QueuedJobWorker resolves it from a
/// fresh scope per job, the same lifetime a request would get.
/// </summary>
public class CollectionExportJob
{
    // Stands in for the latency of a real export pipeline (e.g. a templating/PDF
    // render call) so the "don't block the request" behavior is actually observable.
    private static readonly TimeSpan SimulatedRenderDelayPerQuote = TimeSpan.FromMilliseconds(200);

    private readonly ICollectionRepository _collectionRepository;
    private readonly IQuoteRepository _quoteRepository;
    private readonly IJobStatusStore _statusStore;
    private readonly ILogger<CollectionExportJob> _logger;

    public CollectionExportJob(
        ICollectionRepository collectionRepository,
        IQuoteRepository quoteRepository,
        IJobStatusStore statusStore,
        ILogger<CollectionExportJob> logger)
    {
        _collectionRepository = collectionRepository;
        _quoteRepository = quoteRepository;
        _statusStore = statusStore;
        _logger = logger;
    }

    public async Task RunAsync(Guid jobId, int collectionId, CancellationToken ct)
    {
        _statusStore.MarkProcessing(jobId);

        try
        {
            var collection = await _collectionRepository.GetByIdAsync(collectionId, ct);
            if (collection is null)
            {
                _statusStore.MarkFailed(jobId, $"Collection {collectionId} was not found.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Export for collection '{collection.Name}' (owner: {collection.OwnerId})");
            report.AppendLine($"Generated at {DateTimeOffset.UtcNow:O}");
            report.AppendLine(new string('-', 40));

            foreach (var item in collection.Items)
            {
                var quote = await _quoteRepository.GetQuoteByIdAsync(item.QuoteId, ct);
                if (quote is null)
                {
                    continue;
                }

                await Task.Delay(SimulatedRenderDelayPerQuote, ct);
                report.AppendLine($"\"{quote.Text}\" - {quote.Author}");
            }

            _statusStore.MarkCompleted(jobId, report.ToString());
        }
        catch (OperationCanceledException)
        {
            _statusStore.MarkFailed(jobId, "Job was cancelled because the application was shutting down.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collection export job {JobId} for collection {CollectionId} failed.", jobId, collectionId);
            _statusStore.MarkFailed(jobId, "The export failed due to an unexpected error.");
        }
    }
}
