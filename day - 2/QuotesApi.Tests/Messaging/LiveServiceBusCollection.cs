using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// All live-Service-Bus test classes share one xunit collection so they run
/// sequentially against each other (xunit parallelizes across collections, not
/// within one) - they all bind competing workers to the SAME live
/// "export-processing" subscription, so running two of these classes at once
/// would let one test's worker steal another test's message. Each test method
/// still gets its own isolated SQLite file and its own worker instances via
/// IAsyncLifetime; this only serializes access to the shared subscription.
/// </summary>
[CollectionDefinition("Live Service Bus")]
public class LiveServiceBusCollection : ICollectionFixture<LiveServiceBusFixture>
{
}
