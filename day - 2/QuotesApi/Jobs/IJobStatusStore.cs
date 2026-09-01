namespace QuotesApi.Jobs;

/// <summary>
/// Tracks the lifecycle of background jobs so callers can poll for a result
/// instead of waiting on the HTTP request that queued the job.
/// </summary>
public interface IJobStatusStore
{
    void MarkQueued(Guid jobId);
    void MarkProcessing(Guid jobId);
    void MarkCompleted(Guid jobId, string result);
    void MarkFailed(Guid jobId, string error);
    JobRecord? Get(Guid jobId);
}
