namespace QuotesApi.Messaging;

public interface ICollectionExportEventPublisher
{
    /// <summary>
    /// Publishes a CollectionExportRequestedEvent to the collection-exports topic.
    /// The Service Bus message id is set to <paramref name="messageId"/> so it can
    /// double as the job id returned to the caller and the idempotency key used
    /// by consumers.
    /// </summary>
    Task PublishAsync(string messageId, CollectionExportRequestedEvent @event, CancellationToken ct);
}
