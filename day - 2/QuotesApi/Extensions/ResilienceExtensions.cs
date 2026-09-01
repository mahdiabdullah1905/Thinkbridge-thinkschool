using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace QuotesApi.Extensions;

public static class ResilienceExtensions
{
    public const string ExternalServiceClientName = "external-service";

    // Reusable resilience baseline for any future outbound HttpClient call (Entra ID, a third-party
    // API, etc.). Not currently consumed by any endpoint - QuotesApi has no outbound HTTP call today;
    // Key Vault, Azure Identity and the Azure Monitor exporter already use Azure.Core's own retry
    // pipeline and are deliberately left untouched.
    public static void AddResilientExternalServiceClient(this IServiceCollection services)
    {
        services.AddHttpClient(ExternalServiceClientName)
            .AddResilienceHandler("default", (builder, context) =>
            {
                var logger = context.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("QuotesApi.Resilience.ExternalService");

                // Total timeout bounds retries + circuit breaker combined.
                builder.AddTimeout(TimeSpan.FromSeconds(10));

                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    // Base delay kept short (1s) so 3 exponential retries (~1s+2s+4s worst case)
                    // plus actual request time still fit inside the 10s total timeout above.
                    Delay = TimeSpan.FromSeconds(1),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Retry {AttemptNumber} for {RequestUri} after {DelayMs}ms. Reason: {Reason}",
                            args.AttemptNumber + 1,
                            args.Outcome.Result?.RequestMessage?.RequestUri,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                        return ValueTask.CompletedTask;
                    }
                });

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 10
                });
            });
    }
}
