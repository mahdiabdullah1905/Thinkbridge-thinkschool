using MediatR;
using QuotesApi.Models;

namespace QuotesApi.Queries;

public record GetQuoteListDapperQuery(int Page, int Size) : IRequest<PaginatedResponse<QuoteListItem>>;
