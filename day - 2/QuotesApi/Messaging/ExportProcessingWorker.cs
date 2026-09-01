using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Jobs;

namespace QuotesApi.Messaging;

/// <summary>
/// One instance of this class is registered per competing worker (see
/// ProgramExtensions.AddServiceBusConsumers) - each wraps its own
/// ServiceBusProcessor against the SAME subscription, so Service Bus hands out
/// each message to whichever instance's link asks for it first. That's the
/// competing-consumer behavior: N workers, each message processed by exactly one.
/// </summary>
public class ExportProcessingWorker : BackgroundService
{
    private const string ConsumerName = "export-processing";

    private readonly ServiceBusClient _client;
    private readonly ServiceBusOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportProcessingWorker> _logger;
    private readonly string _workerName;
    private ServiceBusProcessor? _processor;

    public ExportProcessingWorker(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<ExportProcessingWorker> logger,
        string workerName)
    {
        _client = client;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _workerName = workerName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_options.TopicName, _options.ProcessingSubscriptionName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = true
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("[{Worker}] started processing subscription '{Subscription}'.", _workerName, _options.ProcessingSubscriptionName);

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
            _logger.LogInformation("[{Worker}] stopped processing.", _workerName);
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;

        using var scope = _scopeFactory.CreateScope();
        var processedStore = scope.ServiceProvider.GetRequiredService<IProcessedMessageStore>();

        if (await processedStore.IsProcessedAsync(ConsumerName, messageId, args.CancellationToken))
        {
            _logger.LogInformation(
                "[{Worker}] message {MessageId} was already processed by '{Consumer}' (delivery count {DeliveryCount}); skipping duplicate.",
                _workerName, messageId, ConsumerName, args.Message.DeliveryCount);
            return;
        }

        CollectionExportRequestedEvent @event;
        try
        {
            @event = JsonSerializer.Deserialize<CollectionExportRequestedEvent>(args.Message.Body.ToArray())
                     ?? throw new InvalidOperationException("Message body deserialized to null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{Worker}] message {MessageId} has an unreadable body (delivery count {DeliveryCount}); this is a poison message.",
                _workerName, messageId, args.Message.DeliveryCount);
            throw;
        }

        // The app's own publisher always sets MessageId = jobId.ToString(), so this is
        // normally a parse of our own value. But the MessageId is Service Bus's - not
        // this app's - concept, and nothing about the topic contract guarantees every
        // message on it came from that publisher (a manually sent message, or one from
        // a future second producer, would not be a Guid). Falling back to a fresh id
        // instead of throwing keeps a non-Guid MessageId from being treated as a poison
        // message purely because of this consumer's own internal bookkeeping choice.
        var jobId = Guid.TryParse(messageId, out var parsedJobId) ? parsedJobId : Guid.NewGuid();
        var exportJob = scope.ServiceProvider.GetRequiredService<CollectionExportJob>();

        try
        {
            await exportJob.RunAsync(jobId, @event.CollectionId, args.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-message. AutoCompleteMessages only fires when the handler
            // returns without the message already being settled, so explicitly
            // abandoning here - with CancellationToken.None, since the whole point is
            // to still release the lock while the app is shutting down - releases the
            // message for redelivery immediately instead of leaving it PeekLock'd for
            // up to the subscription's full LockDuration.
            _logger.LogWarning(
                "[{Worker}] message {MessageId} processing was cancelled (shutdown); abandoning for redelivery.",
                _workerName, messageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None);
            return;
        }

        var recorded = await processedStore.TryMarkProcessedAsync(ConsumerName, messageId, _workerName, args.CancellationToken);
        if (!recorded)
        {
            _logger.LogWarning(
                "[{Worker}] message {MessageId} was recorded as processed by another delivery while this one was still running.",
                _workerName, messageId);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "[{Worker}] Service Bus processor error. Source={ErrorSource}, Entity={EntityPath}",
            _workerName, args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // base.StopAsync cancels the stoppingToken ExecuteAsync is waiting on and awaits
        // that method's own completion - including its `finally` block, which calls
        // _processor.StopProcessingAsync while the processor is still alive. Disposing
        // the processor here BEFORE that finally block runs made it throw
        // ObjectDisposedException instead, which is fatal to the whole host by default
        // (BackgroundServiceExceptionBehavior.StopHost) - a graceful shutdown request
        // must never itself produce an unhandled exception. Dispose only after
        // ExecuteAsync has fully finished with the processor.
        await base.StopAsync(cancellationToken);

        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }
}
