using MediatR;
using QuotesApi.Models;

namespace QuotesApi.Commands;

public record CreateQuoteCommand(string Author, string Text) : IRequest<Result<Quote>>;
