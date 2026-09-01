using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;

namespace QuotesApi.Tests;

public class GetQuoteListQueryHandlerTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly GetQuoteListQueryHandler _handler;

    public GetQuoteListQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _handler = new GetQuoteListQueryHandler(_context);
    }

    [Fact]
    public async Task Handle_ComputesAuthorQuoteCount_AcrossAllOfThatAuthorsQuotes()
    {
        // Arrange - three quotes from the same author, one from another
        Seed("Maya Angelou", "Quote one");
        Seed("Maya Angelou", "Quote two");
        Seed("Maya Angelou", "Quote three");
        Seed("Rumi", "A single quote");
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetQuoteListQuery(1, 10), CancellationToken.None);

        // Assert
        var mayaItems = response.Items.Where(i => i.Author == "Maya Angelou").ToList();
        Assert.Equal(3, mayaItems.Count);
        Assert.All(mayaItems, item => Assert.Equal(3, item.AuthorQuoteCount));

        var rumiItem = response.Items.Single(i => i.Author == "Rumi");
        Assert.Equal(1, rumiItem.AuthorQuoteCount);
    }

    [Fact]
    public async Task Handle_TruncatesTextLongerThan120Characters_ForThePreview()
    {
        // Arrange
        var longText = new string('a', 150);
        Seed("Author", longText);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetQuoteListQuery(1, 10), CancellationToken.None);

        // Assert
        var item = response.Items.Single();
        Assert.Equal(123, item.TextPreview.Length); // 120 chars + "..."
        Assert.EndsWith("...", item.TextPreview);
    }

    [Fact]
    public async Task Handle_LeavesShortTextUntouched()
    {
        // Arrange
        Seed("Author", "Short quote");
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetQuoteListQuery(1, 10), CancellationToken.None);

        // Assert
        Assert.Equal("Short quote", response.Items.Single().TextPreview);
    }

    [Fact]
    public async Task Handle_RespectsPageAndSize_AndReturnsTrueTotalCount()
    {
        // Arrange - 5 quotes, page size 2
        for (var i = 0; i < 5; i++)
        {
            Seed($"Author {i}", "Text");
        }
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetQuoteListQuery(2, 2), CancellationToken.None);

        // Assert
        Assert.Equal(2, response.Page);
        Assert.Equal(2, response.Size);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count());
    }

    private void Seed(string author, string text)
    {
        _context.Quotes.Add(Quote.Create(author, text).Value!);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
