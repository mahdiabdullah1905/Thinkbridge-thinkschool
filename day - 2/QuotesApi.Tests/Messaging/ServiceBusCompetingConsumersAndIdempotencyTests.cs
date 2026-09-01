using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Messaging;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// Runs two real ExportProcessingWorker instances - not two threads faking it -
/// against the live "export-processing" subscription, bypassing HTTP entirely
/// since this is about the messaging/idempotency plumbing, not the API surface.
/// </summary>
[Collection("Live Service Bus")]
public class ServiceBusCompetingConsumersAndIdempotencyTests : IAsyncLifetime
{
    private const string ConsumerName = "export-processing";

    private readonly LiveServiceBusFixture _serviceBus;
    private MessagingTestHost _host = null!;
    private ExportProcessingWorker _worker1 = null!;
    private ExportProcessingWorker _worker2 = null!;

    public ServiceBusCompetingConsumersAndIdempotencyTests(LiveServiceBusFixture serviceBus)
    {
        _serviceBus = serviceBus;
    }

    public async Task InitializeAsync()
    {
        if (!_serviceBus.IsAvailable) return;

        _host = MessagingTestHost.Create(_serviceBus.Client);
        _worker1 = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(_host.Services, "worker-1");
        _worker2 = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(_host.Services, "worker-2");
        await _worker1.StartAsync(CancellationToken.None);
        await _worker2.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (!_serviceBus.IsAvailable) return;

        await _worker1.StopAsync(CancellationToken.None);
        await _worker2.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    private async Task<int> SeedCollectionAsync()
    {
        var quote = Quote.Create("Competing Consumer Author", "A quote used purely to give the export something to read.").Value!;
        _host.Db.Quotes.Add(quote);
        await _host.Db.SaveChangesAsync();

        var collection = new Collection("Competing Consumers Test Collection", "owner-competing-tests");
        collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
        _host.Db.Collections.Add(collection);
        await _host.Db.SaveChangesAsync();

        return collection.Id;
    }

    private async Task PublishAsync(string messageId, int collectionId)
    {
        await using var sender = _serviceBus.Client.CreateSender(LiveServiceBusFixture.TopicName);
        var @event = new CollectionExportRequestedEvent(collectionId, DateTimeOffset.UtcNow);
        await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event))
        {
            MessageId = messageId,
            ContentType = "application/json"
        });
    }

    private static async Task<T> PollUntilAsync<T>(Func<Task<T>> poll, Func<T, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var result = await poll();
        while (!isDone(result) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            result = await poll();
        }
        return result;
    }

    [SkippableFact]
    public async Task MultipleMessages_AreDistributedAcrossBothCompetingWorkers()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        var collectionId = await SeedCollectionAsync();
        var messageIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid().ToString()).ToList();

        foreach (var id in messageIds)
        {
            await PublishAsync(id, collectionId);
        }

        var rows = await PollUntilAsync(
            () => _host.Db.ProcessedMessages.AsNoTracking()
                .Where(p => p.ConsumerName == ConsumerName && messageIds.Contains(p.MessageId))
                .ToListAsync(),
            list => list.Count == messageIds.Count,
            TimeSpan.FromSeconds(30));

        Assert.Equal(messageIds.Count, rows.Count);

        var distinctWorkers = rows.Select(r => r.ProcessedByWorker).Distinct().ToList();
        Assert.True(distinctWorkers.Count >= 2,
            $"Expected messages to be split across both competing workers, but saw only: {string.Join(", ", distinctWorkers)}");
    }

    [SkippableFact]
    public async Task DuplicateDeliveryOfTheSameMessageId_IsProcessedOnlyOnce()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        var collectionId = await SeedCollectionAsync();
        var messageId = Guid.NewGuid().ToString();

        await PublishAsync(messageId, collectionId);
        var firstDeliveryProcessed = await PollUntilAsync(
            () => _host.Db.ProcessedMessages.AsNoTracking()
                .AnyAsync(p => p.ConsumerName == ConsumerName && p.MessageId == messageId),
            found => found,
            TimeSpan.FromSeconds(15));
        Assert.True(firstDeliveryProcessed, "The first delivery should have been processed within 15 seconds.");

        // Same MessageId sent a second time as an independent send. Duplicate detection
        // is not enabled on this topic (see the Day 19 report for why), so this really
        // is a second delivery - exactly what an at-least-once redelivery looks like to
        // the consumer - and the handler, not the broker, must catch it.
        await PublishAsync(messageId, collectionId);
        await Task.Delay(TimeSpan.FromSeconds(5));

        var count = await _host.Db.ProcessedMessages.AsNoTracking()
            .CountAsync(p => p.ConsumerName == ConsumerName && p.MessageId == messageId);

        Assert.Equal(1, count);
    }
}
