namespace QuotesApi.Messaging;

/// <summary>
/// Published when a collection export is requested. The Service Bus message's
/// own MessageId (set by the publisher) is the job id and the idempotency key -
/// it is not duplicated inside this body.
/// </summary>
public record CollectionExportRequestedEvent(int CollectionId, DateTimeOffset RequestedAtUtc);
