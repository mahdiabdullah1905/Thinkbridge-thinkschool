namespace QuotesApi.Models;

// Read-side shape for the "browse quotes" screen: a short preview instead of
// the full text, plus AuthorQuoteCount, a cross-row aggregate that has no
// place on the Quote write model (it isn't a fact about one quote, and
// storing/updating it on every row would mean touching every quote by that
// author whenever one is added or removed).
public record QuoteListItem(int Id, string Author, string TextPreview, int AuthorQuoteCount);
