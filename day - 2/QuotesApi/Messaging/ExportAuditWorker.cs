using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// The second subscription's consumer. Deliberately does something different
/// from ExportProcessingWorker (just records that the event was seen, instead of
/// generating the report) so the fan-out is a real, independently-observable
/// second consumer rather than a copy of the first.
/// </summary>
public class ExportAuditWorker : BackgroundService
{
    private const string ConsumerName = "export-audit-log";

    private readonly ServiceBusClient _client;
    private readonly ServiceBusOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportAuditWorker> _logger;
    private ServiceBusProcessor? _processor;

    public ExportAuditWorker(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ExportAuditWorker> logger)
    {
        _client = client;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_options.TopicName, _options.AuditSubscriptionName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = true
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("[audit] started processing subscription '{Subscription}'.", _options.AuditSubscriptionName);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            _logger.LogInformation("[audit] stopped processing.");
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processedStore = scope.ServiceProvider.GetRequiredService<IProcessedMessageStore>();

        if (await processedStore.IsProcessedAsync(ConsumerName, messageId, args.CancellationToken))
        {
            _logger.LogInformation("[audit] message {MessageId} already recorded; skipping duplicate.", messageId);
            return;
        }

        var @event = JsonSerializer.Deserialize<CollectionExportRequestedEvent>(args.Message.Body.ToArray())
                     ?? throw new InvalidOperationException("Message body deserialized to null.");

        // Both writes go through the same DbContext/SaveChanges call so they commit
        // atomically - if this crashed between the two, a redelivery could otherwise
        // record a second audit row for a message already believed "processed".
        db.ExportAuditEntries.Add(new ExportAuditEntry
        {
            MessageId = messageId,
            CollectionId = @event.CollectionId,
            ObservedAtUtc = DateTimeOffset.UtcNow
        });
        db.ProcessedMessages.Add(new Models.ProcessedMessage
        {
            ConsumerName = ConsumerName,
            MessageId = messageId,
            ProcessedByWorker = "audit-worker",
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(args.CancellationToken);

        _logger.LogInformation("[audit] recorded export request for collection {CollectionId} (message {MessageId}).", @event.CollectionId, messageId);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "[audit] Service Bus processor error. Source={ErrorSource}, Entity={EntityPath}", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // See ExportProcessingWorker.StopAsync for why base.StopAsync must run first:
        // it's what lets ExecuteAsync's own finally block call StopProcessingAsync while
        // the processor is still alive, instead of racing a premature Dispose here.
        await base.StopAsync(cancellationToken);

        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }
}
