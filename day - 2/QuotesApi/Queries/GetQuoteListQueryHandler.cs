using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Queries;

// Read path: goes straight at AppDbContext (no repository indirection) and
// projects directly to the read model, because the shape it needs -
// truncated preview text plus a per-author quote count - never needs to be
// tracked or updated, only displayed.
public class GetQuoteListQueryHandler : IRequestHandler<GetQuoteListQuery, PaginatedResponse<QuoteListItem>>
{
    private const int PreviewLength = 120;

    private readonly AppDbContext _context;

    public GetQuoteListQueryHandler(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedResponse<QuoteListItem>> Handle(GetQuoteListQuery request, CancellationToken cancellationToken)
    {
        var totalCount = await _context.Quotes.CountAsync(cancellationToken);

        var items = await _context.Quotes
            .OrderBy(q => q.Id)
            .Skip((request.Page - 1) * request.Size)
            .Take(request.Size)
            .Select(q => new QuoteListItem(
                q.Id,
                q.Author,
                q.Text.Length <= PreviewLength ? q.Text : q.Text.Substring(0, PreviewLength) + "...",
                _context.Quotes.Count(other => other.Author == q.Author)))
            .ToListAsync(cancellationToken);

        return new PaginatedResponse<QuoteListItem>
        {
            Page = request.Page,
            Size = request.Size,
            TotalCount = totalCount,
            Items = items
        };
    }
}
