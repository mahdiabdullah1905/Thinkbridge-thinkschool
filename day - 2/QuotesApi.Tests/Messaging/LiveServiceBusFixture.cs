using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Xunit;

namespace QuotesApi.Tests.Messaging;

/// <summary>
/// These tests deliberately hit the real Azure Service Bus namespace created for
/// Day 19 rather than a mock, per the task's requirement for genuine verification.
/// That means they need `az login` (or another credential in the DefaultAzureCredential
/// chain) with the two "Azure Service Bus Data Sender/Receiver" roles on the
/// namespace. On a machine without that, InitializeAsync sets IsAvailable = false
/// and every test in a class using this fixture calls Skip.IfNot(fixture.IsAvailable, ...)
/// so `dotnet test` stays green there instead of failing on an environment gap.
/// </summary>
public class LiveServiceBusFixture : IAsyncLifetime
{
    public const string FullyQualifiedNamespace = "sb-thinkschool-quotes.servicebus.windows.net";
    public const string TopicName = "collection-exports";
    public const string ProcessingSubscription = "export-processing";
    public const string AuditSubscription = "export-audit-log";

    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "";
    public ServiceBusClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true
            });

            Client = new ServiceBusClient(FullyQualifiedNamespace, credential);

            var admin = new ServiceBusAdministrationClient(FullyQualifiedNamespace, credential);
            await admin.GetSubscriptionAsync(TopicName, ProcessingSubscription);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Live Service Bus namespace '{FullyQualifiedNamespace}' not reachable ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }
    }
}
