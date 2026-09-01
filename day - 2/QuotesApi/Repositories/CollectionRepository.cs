using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<CollectionRepository> _logger;

    public CollectionRepository(AppDbContext context, ILogger<CollectionRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Collection?> GetByIdAsync(int id, CancellationToken ct)
    {
        _logger.LogInformation("Getting collection by id {Id}", id);
        return await _context.Collections
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task AddAsync(Collection collection, CancellationToken ct)
    {
        _logger.LogInformation("Adding new collection {Name}", collection.Name);
        _context.Collections.Add(collection);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken ct)
    {
        _logger.LogInformation("Updating collection {Id}", collection.Id);
        _context.Collections.Update(collection);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Collection collection, CancellationToken ct)
    {
        _logger.LogInformation("Deleting collection {Id}", collection.Id);
        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync(ct);
    }
}
