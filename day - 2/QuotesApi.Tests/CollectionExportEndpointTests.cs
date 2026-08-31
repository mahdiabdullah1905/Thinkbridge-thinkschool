using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Jobs;
using QuotesApi.Models;
using QuotesApi.Repositories;
using Xunit;

namespace QuotesApi.Tests;

public class CollectionExportEndpointTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CollectionExportEndpointTests(TestingWebApplicationFactory factory)
    {
        var dbName = $"Data Source=test_export_{Guid.NewGuid()}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(dbName));
            });
        });

        _client = _factory.CreateClient();
    }

    private async Task<(int CollectionId, string QuoteText, string Author)> SeedCollectionWithQuotesAsync(int quoteCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var quotes = new List<Quote>();
        for (var i = 0; i < quoteCount; i++)
        {
            var quote = Quote.Create($"Author {i}", $"Quote text number {i}").Value!;
            quotes.Add(quote);
        }
        db.Quotes.AddRange(quotes);
        await db.SaveChangesAsync();

        var collection = new Collection("Export Test Collection", "owner-export-tests");
        foreach (var quote in quotes)
        {
            collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
        }
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        return (collection.Id, quotes[0].Text, quotes[0].Author);
    }

    private async Task<JobRecord> PollUntilFinishedAsync(Guid jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/api/jobs/{jobId}");
            response.EnsureSuccessStatusCode();
            var record = await response.Content.ReadFromJsonAsync<JobRecord>();
            Assert.NotNull(record);

            if (record!.Status is JobStatus.Completed or JobStatus.Failed)
            {
                return record;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Job {jobId} did not finish within {timeout}.");
    }

    [Fact]
    public async Task PostExport_ReturnsAccepted_WithoutWaitingForTheBackgroundJobToFinish()
    {
        // A wall-clock budget is unreliable when many test classes build hosts in
        // parallel, so prove the ordering directly: gate the job's repository call
        // and show the HTTP response already came back while the job is still
        // stuck behind that gate.
        var reachedRepository = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();
        var gatedCollection = new Collection("Gated Collection", "owner-gate-test");

        var gatedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddScoped<ICollectionRepository>(_ =>
                    new GatedCollectionRepository(reachedRepository, releaseGate.Task, gatedCollection));
            });
        });
        var client = gatedFactory.CreateClient();

        var response = await client.PostAsync("/api/collections/1/export", content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonJobId>();

        // The response above already completed. Now confirm the worker reaches the
        // gated call independently, on its own background execution.
        var reached = await Task.WhenAny(reachedRepository.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(reachedRepository.Task, reached);

        var statusResponse = await client.GetAsync($"/api/jobs/{body!.JobId}");
        var record = await statusResponse.Content.ReadFromJsonAsync<JobRecord>();
        Assert.True(record!.Status is JobStatus.Queued or JobStatus.Processing,
            $"Expected the job to still be in-flight (proving the request didn't wait for it), but it was {record.Status}.");

        releaseGate.SetResult();
    }

    [Fact]
    public async Task PostExport_QueuedJob_IsActuallyProcessedInTheBackground()
    {
        var (collectionId, quoteText, author) = await SeedCollectionWithQuotesAsync(quoteCount: 1);

        var response = await _client.PostAsJsonAsync($"/api/collections/{collectionId}/export", new { });
        var body = await response.Content.ReadFromJsonAsync<JsonJobId>();
        Assert.NotNull(body);

        var record = await PollUntilFinishedAsync(body!.JobId, TimeSpan.FromSeconds(10));

        Assert.Equal(JobStatus.Completed, record.Status);
        Assert.NotNull(record.Result);
        Assert.Contains(quoteText, record.Result);
        Assert.Contains(author, record.Result);
    }

    [Fact]
    public async Task PostExport_MultipleQueuedJobs_AreAllProcessed()
    {
        var (collectionAId, _, _) = await SeedCollectionWithQuotesAsync(quoteCount: 1);
        var (collectionBId, _, _) = await SeedCollectionWithQuotesAsync(quoteCount: 1);

        var responseA = await _client.PostAsync($"/api/collections/{collectionAId}/export", content: null);
        var responseB = await _client.PostAsync($"/api/collections/{collectionBId}/export", content: null);

        var jobA = await responseA.Content.ReadFromJsonAsync<JsonJobId>();
        var jobB = await responseB.Content.ReadFromJsonAsync<JsonJobId>();

        var recordA = await PollUntilFinishedAsync(jobA!.JobId, TimeSpan.FromSeconds(10));
        var recordB = await PollUntilFinishedAsync(jobB!.JobId, TimeSpan.FromSeconds(10));

        Assert.Equal(JobStatus.Completed, recordA.Status);
        Assert.Equal(JobStatus.Completed, recordB.Status);
    }

    [Fact]
    public async Task PostExport_ForNonexistentCollection_FailsTheJobWithoutCrashingTheWorker()
    {
        var response = await _client.PostAsync("/api/collections/999999/export", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonJobId>();

        var record = await PollUntilFinishedAsync(body!.JobId, TimeSpan.FromSeconds(10));
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.NotNull(record.Error);

        // Prove the worker is still alive: a subsequent, valid export still completes.
        var (collectionId, _, _) = await SeedCollectionWithQuotesAsync(quoteCount: 1);
        var followUpResponse = await _client.PostAsync($"/api/collections/{collectionId}/export", content: null);
        var followUpBody = await followUpResponse.Content.ReadFromJsonAsync<JsonJobId>();
        var followUpRecord = await PollUntilFinishedAsync(followUpBody!.JobId, TimeSpan.FromSeconds(10));

        Assert.Equal(JobStatus.Completed, followUpRecord.Status);
    }

    private class JsonJobId
    {
        public Guid JobId { get; set; }
    }

    private class GatedCollectionRepository : ICollectionRepository
    {
        private readonly TaskCompletionSource _reached;
        private readonly Task _gate;
        private readonly Collection _collection;

        public GatedCollectionRepository(TaskCompletionSource reached, Task gate, Collection collection)
        {
            _reached = reached;
            _gate = gate;
            _collection = collection;
        }

        public async Task<Collection?> GetByIdAsync(int id, CancellationToken ct)
        {
            _reached.TrySetResult();
            await _gate;
            return _collection;
        }

        public Task AddAsync(Collection collection, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateAsync(Collection collection, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAsync(Collection collection, CancellationToken ct) => throw new NotImplementedException();
    }
}
