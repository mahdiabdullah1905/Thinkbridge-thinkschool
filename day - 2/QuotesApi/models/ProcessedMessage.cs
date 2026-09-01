namespace QuotesApi.Models;

/// <summary>
/// Records that a given Service Bus message has already been handled by a given
/// consumer, keyed by (ConsumerName, MessageId) so the same physical message can
/// legitimately be processed once per subscription without being flagged as a
/// duplicate of itself across subscriptions.
/// </summary>
public class ProcessedMessage
{
    public string ConsumerName { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string? ProcessedByWorker { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
