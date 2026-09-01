namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; private set; }
    public string Author { get; private set; }
    public string Text { get; private set; }
    public bool IsDeleted { get; private set; }

    // Required for EF Core
    private Quote() 
    {
        Author = "";
        Text = "";
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
    }

    public static Result<Quote> Create(string author, string text)
    {
        if (string.IsNullOrWhiteSpace(author))
            return new Result<Quote>(null, "Author cannot be null or empty.");
        if (author.Length > 200)
            return new Result<Quote>(null, "Author cannot exceed 200 characters.");
        
        if (string.IsNullOrWhiteSpace(text))
            return new Result<Quote>(null, "Text cannot be null or empty.");
        if (text.Length > 1000)
            return new Result<Quote>(null, "Text cannot exceed 1000 characters.");

        return new Result<Quote>(new Quote(author, text), null);
    }

    public void Delete()
    {
        IsDeleted = true;
    }
}