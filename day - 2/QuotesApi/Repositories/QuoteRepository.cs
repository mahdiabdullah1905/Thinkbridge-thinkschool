using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<QuoteRepository> _logger;

    public QuoteRepository(AppDbContext context, ILogger<QuoteRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(IEnumerable<Quote> Quotes, int TotalCount)> GetQuotesAsync(int page, int size, CancellationToken ct)
    {
        _logger.LogInformation("Getting quotes page {Page} with size {Size}", page, size);
        
        var totalCount = await _context.Quotes.CountAsync(ct);
        
        var quotes = await _context.Quotes
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
            
        return (quotes, totalCount);
    }

    public async Task<Quote?> GetQuoteByIdAsync(int id, CancellationToken ct)
    {
        _logger.LogInformation("Getting quote by id {Id}", id);
        return await _context.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task AddQuoteAsync(Quote quote, CancellationToken ct)
    {
        _logger.LogInformation("Adding quote by author {Author}", quote.Author);
        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteQuoteAsync(Quote quote, CancellationToken ct)
    {
        _logger.LogInformation("Deleting quote with id {Id}", quote.Id);
        _context.Quotes.Remove(quote);
        await _context.SaveChangesAsync(ct);
    }
}
