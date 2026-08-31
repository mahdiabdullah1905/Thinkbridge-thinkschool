using System.Collections.Concurrent;

namespace QuotesApi.Jobs;

/// <summary>
/// In-memory job status tracker. Good enough for a single-instance API; see the
/// background-jobs report for what changes if this needs to survive a restart
/// or be shared across multiple instances.
/// </summary>
public class JobStatusStore : IJobStatusStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public void MarkQueued(Guid jobId) =>
        _jobs[jobId] = new JobRecord(jobId, JobStatus.Queued, null, null, DateTimeOffset.UtcNow);

    public void MarkProcessing(Guid jobId) =>
        _jobs.AddOrUpdate(
            jobId,
            _ => new JobRecord(jobId, JobStatus.Processing, null, null, DateTimeOffset.UtcNow),
            (_, existing) => existing with { Status = JobStatus.Processing, UpdatedAt = DateTimeOffset.UtcNow });

    public void MarkCompleted(Guid jobId, string result) =>
        _jobs.AddOrUpdate(
            jobId,
            _ => new JobRecord(jobId, JobStatus.Completed, result, null, DateTimeOffset.UtcNow),
            (_, existing) => existing with { Status = JobStatus.Completed, Result = result, UpdatedAt = DateTimeOffset.UtcNow });

    public void MarkFailed(Guid jobId, string error) =>
        _jobs.AddOrUpdate(
            jobId,
            _ => new JobRecord(jobId, JobStatus.Failed, null, error, DateTimeOffset.UtcNow),
            (_, existing) => existing with { Status = JobStatus.Failed, Error = error, UpdatedAt = DateTimeOffset.UtcNow });

    public JobRecord? Get(Guid jobId) => _jobs.TryGetValue(jobId, out var record) ? record : null;
}
