using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

/// <summary>
/// Registered as a singleton so the ServiceBusSender (safe for concurrent use,
/// meant to be long-lived per the SDK's own guidance) is created once for the
/// life of the app instead of per request.
/// </summary>
public class CollectionExportEventPublisher : ICollectionExportEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public CollectionExportEventPublisher(ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        _sender = client.CreateSender(options.Value.TopicName);
    }

    public async Task PublishAsync(string messageId, CollectionExportRequestedEvent @event, CancellationToken ct)
    {
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event))
        {
            MessageId = messageId,
            ContentType = "application/json",
            ApplicationProperties = { ["eventType"] = nameof(CollectionExportRequestedEvent) }
        };

        await _sender.SendMessageAsync(message, ct);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
