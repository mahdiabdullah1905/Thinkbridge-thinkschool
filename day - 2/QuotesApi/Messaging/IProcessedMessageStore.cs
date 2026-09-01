namespace QuotesApi.Messaging;

public interface IProcessedMessageStore
{
    Task<bool> IsProcessedAsync(string consumerName, string messageId, CancellationToken ct);

    /// <summary>
    /// Records that (consumerName, messageId) has been handled. Returns false
    /// instead of throwing if another delivery already recorded it first - the
    /// uniqueness is enforced by the table's composite key, not by the earlier
    /// IsProcessedAsync check, so two concurrent competing-consumer deliveries of
    /// the same message can't both "win".
    /// </summary>
    Task<bool> TryMarkProcessedAsync(string consumerName, string messageId, string? processedByWorker, CancellationToken ct);
}
