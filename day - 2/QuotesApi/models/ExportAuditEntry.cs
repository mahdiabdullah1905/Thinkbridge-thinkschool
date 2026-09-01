namespace QuotesApi.Models;

/// <summary>
/// Written only by the export-audit-log subscription's consumer. Its existence
/// for a given MessageId is the evidence that the second subscription received
/// its own independent copy of a published event.
/// </summary>
public class ExportAuditEntry
{
    public int Id { get; set; }
    public string MessageId { get; set; } = "";
    public int CollectionId { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
}
