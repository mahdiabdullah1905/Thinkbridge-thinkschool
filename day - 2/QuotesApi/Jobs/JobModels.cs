namespace QuotesApi.Jobs;

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public record JobRecord(Guid JobId, JobStatus Status, string? Result, string? Error, DateTimeOffset UpdatedAt);
