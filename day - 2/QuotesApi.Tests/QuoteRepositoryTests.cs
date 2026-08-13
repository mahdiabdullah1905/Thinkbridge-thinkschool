using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class QuoteRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly QuoteRepository _repository;

    public QuoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _repository = new QuoteRepository(_context, new NullLogger<QuoteRepository>());
    }

    [Fact]
    public async Task DeleteQuoteAsync_SoftDeletesQuote_AndHidesFromGet()
    {
        // Arrange
        var quote = Quote.Create("Author1", "Text1").Value!;
        await _repository.AddQuoteAsync(quote, CancellationToken.None);
        
        // Assert it was added
        var (quotesBefore, countBefore) = await _repository.GetQuotesAsync(1, 10, CancellationToken.None);
        Assert.Equal(1, countBefore);

        // Act - Soft Delete
        var fetchedQuote = await _repository.GetQuoteByIdAsync(quote.Id, CancellationToken.None);
        Assert.NotNull(fetchedQuote);
        
        fetchedQuote.Delete();
        await _repository.DeleteQuoteAsync(fetchedQuote, CancellationToken.None);

        _context.ChangeTracker.Clear(); // ensure fresh DB read

        // Assert - Should not return in queries
        var (quotesAfter, countAfter) = await _repository.GetQuotesAsync(1, 10, CancellationToken.None);
        Assert.Equal(0, countAfter);
        Assert.Empty(quotesAfter);

        var getByIdResult = await _repository.GetQuoteByIdAsync(quote.Id, CancellationToken.None);
        Assert.Null(getByIdResult);

        // Assert - Physical row still exists
        var rawCount = await _context.Quotes.IgnoreQueryFilters().CountAsync();
        Assert.Equal(1, rawCount);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
