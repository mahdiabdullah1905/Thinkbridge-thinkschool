namespace QuotesApi.Messaging;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>e.g. "sb-thinkschool-quotes.servicebus.windows.net" - a hostname, not a secret.</summary>
    public string FullyQualifiedNamespace { get; set; } = "";
    public string TopicName { get; set; } = "";
    public string ProcessingSubscriptionName { get; set; } = "";
    public string AuditSubscriptionName { get; set; } = "";

    /// <summary>
    /// Lets the consumer hosted services be turned off (e.g. to publish a message
    /// and inspect it sitting in the topic/subscriptions before anything drains it).
    /// Defaults to on.
    /// </summary>
    public bool EnableConsumers { get; set; } = true;
}
