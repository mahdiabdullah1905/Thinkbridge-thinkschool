using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Messaging;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// Sends a genuinely malformed message straight to the topic and proves - by
/// reading the real dead-letter sub-queue, not by asserting on our own logs -
/// that Service Bus itself dead-lettered it after export-processing's configured
/// MaxDeliveryCount (3) was exhausted. Then proves a normal message right after
/// still gets processed, i.e. the poison message didn't wedge anything.
/// </summary>
[Collection("Live Service Bus")]
public class ServiceBusPoisonMessageTests : IAsyncLifetime
{
    private const string ConsumerName = "export-processing";

    private readonly LiveServiceBusFixture _serviceBus;
    private MessagingTestHost _host = null!;
    private ExportProcessingWorker _worker = null!;

    public ServiceBusPoisonMessageTests(LiveServiceBusFixture serviceBus)
    {
        _serviceBus = serviceBus;
    }

    public async Task InitializeAsync()
    {
        if (!_serviceBus.IsAvailable) return;

        _host = MessagingTestHost.Create(_serviceBus.Client);
        _worker = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(_host.Services, "poison-test-worker");
        await _worker.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (!_serviceBus.IsAvailable) return;

        await _worker.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [SkippableFact]
    public async Task PoisonMessage_IsDeadLetteredByServiceBus_AndDoesNotBlockLaterMessages()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        var poisonMessageId = $"poison-{Guid.NewGuid()}";
        await using (var sender = _serviceBus.Client.CreateSender(LiveServiceBusFixture.TopicName))
        {
            // Deliberately not valid JSON: JsonSerializer.Deserialize throws on every delivery
            // attempt, every time, which is exactly what makes it a poison message.
            await sender.SendMessageAsync(new ServiceBusMessage("{ this is not valid json ")
            {
                MessageId = poisonMessageId,
                ContentType = "application/json"
            });
        }

        ServiceBusReceivedMessage? deadLettered = null;
        await using (var dlqReceiver = _serviceBus.Client.CreateReceiver(
                   LiveServiceBusFixture.TopicName,
                   LiveServiceBusFixture.ProcessingSubscription,
                   new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }))
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (deadLettered is null && DateTime.UtcNow < deadline)
            {
                var peeked = await dlqReceiver.PeekMessagesAsync(maxMessages: 50, fromSequenceNumber: 1);
                deadLettered = peeked.FirstOrDefault(m => m.MessageId == poisonMessageId);
                if (deadLettered is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        Assert.NotNull(deadLettered);
        Assert.Equal("MaxDeliveryCountExceeded", deadLettered!.DeadLetterReason);

        // Prove the subscription/worker is still healthy: a normal message sent right
        // after the poison one must still complete normally.
        var collectionId = await SeedCollectionAsync();
        var goodMessageId = Guid.NewGuid().ToString();

        await using (var sender = _serviceBus.Client.CreateSender(LiveServiceBusFixture.TopicName))
        {
            var @event = new CollectionExportRequestedEvent(collectionId, DateTimeOffset.UtcNow);
            await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event))
            {
                MessageId = goodMessageId,
                ContentType = "application/json"
            });
        }

        var deadline2 = DateTime.UtcNow.AddSeconds(15);
        var processed = false;
        while (!processed && DateTime.UtcNow < deadline2)
        {
            processed = await _host.Db.ProcessedMessages.AsNoTracking()
                .AnyAsync(p => p.ConsumerName == ConsumerName && p.MessageId == goodMessageId);
            if (!processed) await Task.Delay(200);
        }

        Assert.True(processed, "A normal message sent after the poison message should still have been processed.");
    }

    private async Task<int> SeedCollectionAsync()
    {
        var quote = Quote.Create("Poison Test Author", "A quote used purely to give the export something to read.").Value!;
        _host.Db.Quotes.Add(quote);
        await _host.Db.SaveChangesAsync();

        var collection = new Collection("Poison Message Test Collection", "owner-poison-tests");
        collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
        _host.Db.Collections.Add(collection);
        await _host.Db.SaveChangesAsync();

        return collection.Id;
    }
}
