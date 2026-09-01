using MediatR;
using QuotesApi.Models;

namespace QuotesApi.Queries;

public record GetQuoteListQuery(int Page, int Size) : IRequest<PaginatedResponse<QuoteListItem>>;
