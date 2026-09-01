using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Jobs;
using QuotesApi.Messaging;
using QuotesApi.Repositories;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// A minimal DI container wiring up just what ExportProcessingWorker/ExportAuditWorker
/// need (repositories, the idempotency store, a scratch SQLite database) without
/// spinning up the whole ASP.NET host - these tests are about the messaging
/// plumbing, not HTTP or auth.
/// </summary>
internal sealed class MessagingTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _assertionScope;
    public string DbPath { get; }

    private MessagingTestHost(ServiceProvider provider, string dbPath)
    {
        _provider = provider;
        DbPath = dbPath;
        _assertionScope = provider.CreateScope();
    }

    public IServiceProvider Services => _provider;

    /// <summary>A single long-lived scope's DbContext, for test assertions only -
    /// the worker itself always creates and disposes its own short-lived scopes.</summary>
    public AppDbContext Db => _assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();

    public static MessagingTestHost Create(ServiceBusClient serviceBusClient)
    {
        var dbPath = $"test_sbmsg_{Guid.NewGuid()}.db";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<CollectionExportJob>();
        services.AddSingleton<IJobStatusStore, JobStatusStore>();
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();
        services.Configure<ServiceBusOptions>(o =>
        {
            o.FullyQualifiedNamespace = LiveServiceBusFixture.FullyQualifiedNamespace;
            o.TopicName = LiveServiceBusFixture.TopicName;
            o.ProcessingSubscriptionName = LiveServiceBusFixture.ProcessingSubscription;
            o.AuditSubscriptionName = LiveServiceBusFixture.AuditSubscription;
        });
        services.AddSingleton(serviceBusClient);

        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        }

        return new MessagingTestHost(provider, dbPath);
    }

    public async ValueTask DisposeAsync()
    {
        _assertionScope.Dispose();
        await _provider.DisposeAsync();

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = DbPath + suffix;
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
            }
        }
    }
}
