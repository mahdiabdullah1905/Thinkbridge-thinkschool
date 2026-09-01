using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// Backed by the same SQLite database as the rest of the app (see AppDbContext).
/// Persistent, so it survives a restart - a message redelivered after the app
/// comes back up is still recognized. The tradeoff: it's a single local file, so
/// it doesn't help once there's more than one API instance sharing the load (see
/// the Day 19 report for what that would need instead).
/// </summary>
public class ProcessedMessageStore : IProcessedMessageStore
{
    private readonly AppDbContext _db;

    public ProcessedMessageStore(AppDbContext db)
    {
        _db = db;
    }

    public Task<bool> IsProcessedAsync(string consumerName, string messageId, CancellationToken ct) =>
        _db.ProcessedMessages.AnyAsync(p => p.ConsumerName == consumerName && p.MessageId == messageId, ct);

    public async Task<bool> TryMarkProcessedAsync(string consumerName, string messageId, string? processedByWorker, CancellationToken ct)
    {
        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            ConsumerName = consumerName,
            MessageId = messageId,
            ProcessedByWorker = processedByWorker,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Composite primary key violation: another delivery already recorded this
            // (consumerName, messageId) first. Detach so this DbContext can still be reused.
            foreach (var entry in _db.ChangeTracker.Entries<ProcessedMessage>())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }
}
