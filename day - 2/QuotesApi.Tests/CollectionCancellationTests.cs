using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Models;
using QuotesApi.Repositories;
using Xunit;

namespace QuotesApi.Tests;

public class CollectionCancellationTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CollectionCancellationTests(TestingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AddCollection_WhenClientCancels_CancelsOperationMidFlight()
    {
        var endpointReached = new TaskCompletionSource();
        var requestCompleted = new TaskCompletionSource();
        
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddScoped<ICollectionRepository>(_ => new BlockingCollectionRepository(endpointReached, requestCompleted));
            });
        }).CreateClient();

        using var cts = new CancellationTokenSource();
        var request = new CreateCollectionRequest { Name = "Never finishes", OwnerId = "user-123" };

        // Start request
        var postTask = client.PostAsJsonAsync("/api/collections", request, cts.Token);

        // Wait until repo is hit to guarantee mid-flight execution
        await endpointReached.Task;

        // Cancel the request while it is waiting in the repository layer
        cts.Cancel();

        // Verify that the request finishes with cancellation (either throws locally or returns 499)
        try
        {
            var response = await postTask;
            Assert.Equal(499, (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            // TestServer transport threw TaskCanceledException when client cancelled. This is also expected behavior.
        }

        // Verify that our mock repository observed the cancellation token correctly
        // We use a short timeout on the await to ensure the test fails fast if cancellation isn't respected
        var completedTask = await Task.WhenAny(requestCompleted.Task, Task.Delay(2000));
        Assert.Equal(requestCompleted.Task, completedTask);
    }

    private class BlockingCollectionRepository : ICollectionRepository
    {
        private readonly TaskCompletionSource _endpointReached;
        private readonly TaskCompletionSource _requestCompleted;

        public BlockingCollectionRepository(TaskCompletionSource endpointReached, TaskCompletionSource requestCompleted)
        {
            _endpointReached = endpointReached;
            _requestCompleted = requestCompleted;
        }

        public async Task AddAsync(Collection collection, CancellationToken ct)
        {
            // Signal the test that the request has reached the repository layer
            _endpointReached.SetResult();
            
            try
            {
                // Indefinitely wait until the cancellation token triggers an OperationCanceledException
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                // Signal the test that cancellation was successfully observed
                _requestCompleted.SetResult();
                throw; // Rethrow to propagate to the middleware
            }
        }

        public Task<Collection?> GetByIdAsync(int id, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateAsync(Collection collection, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteAsync(Collection collection, CancellationToken ct) => throw new NotImplementedException();
    }
}
