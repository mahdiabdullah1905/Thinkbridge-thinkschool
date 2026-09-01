using MediatR;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Commands;

// Write path: validate against the normalized domain model (Quote.Create
// already enforces the author/text rules) and persist through the same
// repository the rest of the write side uses. No read-shaping happens here.
public class CreateQuoteCommandHandler : IRequestHandler<CreateQuoteCommand, Result<Quote>>
{
    private readonly IQuoteRepository _repository;
    private readonly ILogger<CreateQuoteCommandHandler> _logger;

    public CreateQuoteCommandHandler(IQuoteRepository repository, ILogger<CreateQuoteCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<Quote>> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var result = Quote.Create(request.Author, request.Text);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Quote validation failed for author {Author}: {Error}", request.Author, result.Error);
            return result;
        }

        var quote = result.Value!;
        await _repository.AddQuoteAsync(quote, cancellationToken);
        _logger.LogInformation("Saved quote {QuoteId} for author {Author}", quote.Id, quote.Author);

        return result;
    }
}
