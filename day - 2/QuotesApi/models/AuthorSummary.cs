namespace QuotesApi.Models;

public record AuthorSummary(string Author, int QuoteCount, IReadOnlyList<string> Quotes);
