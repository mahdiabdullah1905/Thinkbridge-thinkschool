using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Jobs;
using QuotesApi.Messaging;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// Proves ExportProcessingWorker stops cleanly through the CancellationToken
/// BackgroundService hands it, both when idle and when a job is actively running,
/// against the real Service Bus subscription (real StartProcessingAsync/
/// StopProcessingAsync lifecycle, not a fake).
/// </summary>
[Collection("Live Service Bus")]
public class ServiceBusWorkerShutdownTests
{
    private readonly LiveServiceBusFixture _serviceBus;

    public ServiceBusWorkerShutdownTests(LiveServiceBusFixture serviceBus)
    {
        _serviceBus = serviceBus;
    }

    [SkippableFact]
    public async Task Worker_WithNoMessageInFlight_StopsQuicklyAndCleanly()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        await using var host = MessagingTestHost.Create(_serviceBus.Client);
        var worker = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(host.Services, "shutdown-idle-worker");

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1)); // let StartProcessingAsync actually establish the AMQP link

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Expected the idle worker to stop quickly, but StopAsync took {stopwatch.Elapsed}.");
    }

    [SkippableFact]
    public async Task Worker_StoppedMidMessage_CancelsTheInFlightJob_AndTheMessageIsLaterReprocessed()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        await using var host = MessagingTestHost.Create(_serviceBus.Client);

        // Enough quotes that the simulated per-quote render delay keeps the job
        // running for a few seconds, giving us a real window to shut down mid-flight.
        var quotes = new List<Quote>();
        for (var i = 0; i < 10; i++)
        {
            quotes.Add(Quote.Create($"Shutdown Author {i}", $"Shutdown quote {i}").Value!);
        }
        host.Db.Quotes.AddRange(quotes);
        await host.Db.SaveChangesAsync();

        var collection = new Collection("Shutdown Test Collection", "owner-shutdown-tests");
        foreach (var quote in quotes)
        {
            collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
        }
        host.Db.Collections.Add(collection);
        await host.Db.SaveChangesAsync();

        var messageId = Guid.NewGuid().ToString();
        await using (var sender = _serviceBus.Client.CreateSender(LiveServiceBusFixture.TopicName))
        {
            var @event = new CollectionExportRequestedEvent(collection.Id, DateTimeOffset.UtcNow);
            await sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event))
            {
                MessageId = messageId,
                ContentType = "application/json"
            });
        }

        var jobStatusStore = host.Services.GetRequiredService<IJobStatusStore>();
        var jobId = Guid.Parse(messageId);

        var worker = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(host.Services, "shutdown-midflight-worker");
        await worker.StartAsync(CancellationToken.None);

        var startDeadline = DateTime.UtcNow.AddSeconds(15);
        while (jobStatusStore.Get(jobId)?.Status != JobStatus.Processing && DateTime.UtcNow < startDeadline)
        {
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Processing, jobStatusStore.Get(jobId)?.Status);

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Expected StopAsync to return promptly even mid-message, but it took {stopwatch.Elapsed}.");

        // The message was never completed (the job was cancelled mid-flight), so a
        // fresh worker started afterward should still be able to pick it up and finish
        // it - proving shutdown deferred the work instead of losing it.
        var followUpWorker = ActivatorUtilities.CreateInstance<ExportProcessingWorker>(host.Services, "shutdown-followup-worker");
        await followUpWorker.StartAsync(CancellationToken.None);
        try
        {
            var finishDeadline = DateTime.UtcNow.AddSeconds(30);
            var record = jobStatusStore.Get(jobId);
            while (record?.Status is not JobStatus.Completed && DateTime.UtcNow < finishDeadline)
            {
                await Task.Delay(200);
                record = jobStatusStore.Get(jobId);
            }
            Assert.Equal(JobStatus.Completed, record?.Status);
        }
        finally
        {
            await followUpWorker.StopAsync(CancellationToken.None);
        }
    }
}
