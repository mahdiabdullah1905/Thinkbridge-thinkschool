using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuotesApi.Extensions;

namespace QuotesApi.Tests;

public class ResilientHttpClientTests
{
    // Always returns 503 so every attempt is treated as a transient failure by the default
    // HttpRetryStrategyOptions predicate, forcing all 3 configured retries to run.
    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

        public void Dispose() { }

        private sealed class ListLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }

    [Fact]
    public async Task ExternalServiceClient_OnRepeatedTransientFailure_RetriesThreeTimesAndLogsEachRetry()
    {
        var fakeHandler = new AlwaysUnavailableHandler();
        var loggerProvider = new ListLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        services.AddResilientExternalServiceClient();
        services.AddHttpClient(ResilienceExtensions.ExternalServiceClientName)
            .ConfigurePrimaryHttpMessageHandler(() => fakeHandler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ResilienceExtensions.ExternalServiceClientName);

        var response = await client.GetAsync("https://external-service.invalid/health");

        // 1 initial attempt + 3 retries.
        Assert.Equal(4, fakeHandler.CallCount);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var retryLogs = loggerProvider.Messages
            .Where(m => m.StartsWith("Retry ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, retryLogs.Count);
        Assert.Equal(["Retry 1 ", "Retry 2 ", "Retry 3 "], retryLogs.Select(m => m[..8]));
    }
}
