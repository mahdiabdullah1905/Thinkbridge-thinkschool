using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Jobs;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// Exercises the real POST /api/collections/{id}/export endpoint end-to-end against
/// the live Service Bus namespace: publish, then prove both subscriptions receive
/// their own copy of the message. See LiveServiceBusFixture for how these tests
/// degrade gracefully (skip, not fail) when no live namespace is reachable.
/// </summary>
[Collection("Live Service Bus")]
public class ServiceBusPublishingTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly LiveServiceBusFixture _serviceBus;
    private readonly HttpClient _client;

    public ServiceBusPublishingTests(TestingWebApplicationFactory factory, LiveServiceBusFixture serviceBus)
    {
        _serviceBus = serviceBus;
        var dbName = $"Data Source=test_sbpublish_{Guid.NewGuid()}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(dbName));

                // Ordinary "Testing"-environment hosts never start the live consumers
                // (see Program.cs) - this test explicitly wants them, so it opts in itself.
                services.AddServiceBusConsumers();
            });
        });

        _client = _factory.CreateClient();
    }

    private async Task<int> SeedCollectionWithQuotesAsync(int quoteCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var quotes = new List<Quote>();
        for (var i = 0; i < quoteCount; i++)
        {
            quotes.Add(Quote.Create($"Author {i}", $"Quote text number {i}").Value!);
        }
        db.Quotes.AddRange(quotes);
        await db.SaveChangesAsync();

        var collection = new Collection("SB Publishing Test Collection", "owner-sb-publish-tests");
        foreach (var quote in quotes)
        {
            collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
        }
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        return collection.Id;
    }

    [SkippableFact]
    public async Task PostExport_PublishesAndReturnsAccepted()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        var collectionId = await SeedCollectionWithQuotesAsync(3);

        var response = await _client.PostAsync($"/api/collections/{collectionId}/export", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonJobId>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.JobId);
    }

    [SkippableFact]
    public async Task PostExport_MessageIsDeliveredToBothSubscriptionsIndependently()
    {
        Skip.IfNot(_serviceBus.IsAvailable, _serviceBus.UnavailableReason);

        var collectionId = await SeedCollectionWithQuotesAsync(1);

        var response = await _client.PostAsync($"/api/collections/{collectionId}/export", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonJobId>();
        Assert.NotNull(body);

        // export-processing subscription's own copy: proven by the job actually completing.
        var record = await PollUntilAsync(async () =>
        {
            var jobResponse = await _client.GetAsync($"/api/jobs/{body!.JobId}");
            jobResponse.EnsureSuccessStatusCode();
            return await jobResponse.Content.ReadFromJsonAsync<JobRecord>();
        }, r => r is not null && r.Status is JobStatus.Completed or JobStatus.Failed, TimeSpan.FromSeconds(30));

        Assert.NotNull(record);
        Assert.Equal(JobStatus.Completed, record!.Status);

        // export-audit-log subscription's own, independent copy: a DB row written only
        // by ExportAuditWorker, so its presence proves the second subscription really
        // received and processed the same event on its own.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = await PollUntilAsync(
            () => db.ExportAuditEntries.AsNoTracking().FirstOrDefaultAsync(e => e.MessageId == body!.JobId.ToString()),
            e => e is not null,
            TimeSpan.FromSeconds(15));

        Assert.NotNull(entry);
        Assert.Equal(collectionId, entry!.CollectionId);
    }

    private static async Task<T?> PollUntilAsync<T>(Func<Task<T?>> poll, Func<T?, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        T? result = default;
        while (DateTime.UtcNow < deadline)
        {
            result = await poll();
            if (isDone(result))
            {
                return result;
            }
            await Task.Delay(200);
        }
        return result;
    }

    private class JsonJobId
    {
        public Guid JobId { get; set; }
    }
}
