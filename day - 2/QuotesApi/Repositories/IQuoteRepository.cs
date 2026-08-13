namespace QuotesApi.Repositories;

using QuotesApi.Models;

public interface IQuoteRepository
{
    Task<(IEnumerable<Quote> Quotes, int TotalCount)> GetQuotesAsync(int page, int size, CancellationToken ct);
    Task<Quote?> GetQuoteByIdAsync(int id, CancellationToken ct);
    Task AddQuoteAsync(Quote quote, CancellationToken ct);
    Task DeleteQuoteAsync(Quote quote, CancellationToken ct);
}
