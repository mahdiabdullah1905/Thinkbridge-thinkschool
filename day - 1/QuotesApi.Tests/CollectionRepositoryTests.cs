using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class CollectionRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly CollectionRepository _repository;

    public CollectionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _repository = new CollectionRepository(_context, new NullLogger<CollectionRepository>());
    }

    [Fact]
    public async Task AddAndGetCollection_WithItems_PersistsProperly()
    {
        var collection = new Collection("Test Collection", "owner1");
        collection.AddItem(1);
        collection.AddItem(2);

        await _repository.AddAsync(collection, CancellationToken.None);

        // Clear tracker to ensure fresh DB read
        _context.ChangeTracker.Clear();

        var retrieved = await _repository.GetByIdAsync(collection.Id, CancellationToken.None);
        
        Assert.NotNull(retrieved);
        Assert.Equal("Test Collection", retrieved.Name);
        Assert.Equal(2, retrieved.Items.Count);
        Assert.Contains(retrieved.Items, i => i.QuoteId == 1);
        Assert.Contains(retrieved.Items, i => i.QuoteId == 2);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
