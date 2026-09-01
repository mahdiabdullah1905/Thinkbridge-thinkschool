using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;

namespace QuotesApi.Tests;

// The Dapper handler (GetQuoteListDapperQueryHandler) opens its own SqliteConnection using
// AppDbContext's connection string, so unlike the other handler tests, ":memory:" won't do
// here - each ":memory:" connection is its own isolated database unless it's the exact same
// open connection. A per-test file database (same pattern AuthTests.cs uses for its own
// reasons) makes both handlers actually read the same rows.
public class EfVsDapperQuoteListEquivalenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDbContext _context;
    private readonly GetQuoteListQueryHandler _efHandler;
    private readonly GetQuoteListDapperQueryHandler _dapperHandler;

    public EfVsDapperQuoteListEquivalenceTests()
    {
        _dbPath = $"test_ef_vs_dapper_{Guid.NewGuid()}.db";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.Migrate();

        _efHandler = new GetQuoteListQueryHandler(_context);
        _dapperHandler = new GetQuoteListDapperQueryHandler(_context);
    }

    [Fact]
    public async Task EfAndDapper_ReturnIdenticalPages_ForTheSameAuthorsAndPaging()
    {
        // Arrange - mixed authors/lengths, including text past the 120-char preview cutoff
        Seed("Maya Angelou", "Short one.");
        Seed("Maya Angelou", "Another short quote from the same author.");
        Seed("Maya Angelou", new string('a', 150)); // forces TextPreview truncation
        Seed("Rumi", "A single quote from a different author.");
        Seed("Rumi", "Second Rumi quote.");
        await _context.SaveChangesAsync();

        // Act
        var efResult = await _efHandler.Handle(new GetQuoteListQuery(1, 3), CancellationToken.None);
        var dapperResult = await _dapperHandler.Handle(new GetQuoteListDapperQuery(1, 3), CancellationToken.None);

        // Assert - same paging metadata
        Assert.Equal(efResult.Page, dapperResult.Page);
        Assert.Equal(efResult.Size, dapperResult.Size);
        Assert.Equal(efResult.TotalCount, dapperResult.TotalCount);
        Assert.Equal(5, efResult.TotalCount);

        // Assert - same rows, same order, same shape, field by field (not just count)
        var efItems = efResult.Items.ToList();
        var dapperItems = dapperResult.Items.ToList();
        Assert.Equal(efItems.Count, dapperItems.Count);

        for (var i = 0; i < efItems.Count; i++)
        {
            Assert.Equal(efItems[i].Id, dapperItems[i].Id);
            Assert.Equal(efItems[i].Author, dapperItems[i].Author);
            Assert.Equal(efItems[i].TextPreview, dapperItems[i].TextPreview);
            Assert.Equal(efItems[i].AuthorQuoteCount, dapperItems[i].AuthorQuoteCount);
        }

        // Assert - the truncation case actually happened and matches on both sides
        var truncated = efItems.Single(item => item.TextPreview.EndsWith("..."));
        Assert.Equal(123, truncated.TextPreview.Length);
        Assert.Equal(truncated.TextPreview, dapperItems.Single(item => item.Id == truncated.Id).TextPreview);
    }

    [Fact]
    public async Task EfAndDapper_AgreeOnSecondPage()
    {
        // Arrange - 5 rows, page size 2, so page 2 is a partial-overlap case worth checking on its own
        for (var i = 0; i < 5; i++)
        {
            Seed($"Author {i}", $"Quote number {i}");
        }
        await _context.SaveChangesAsync();

        // Act
        var efResult = await _efHandler.Handle(new GetQuoteListQuery(2, 2), CancellationToken.None);
        var dapperResult = await _dapperHandler.Handle(new GetQuoteListDapperQuery(2, 2), CancellationToken.None);

        // Assert
        var efIds = efResult.Items.Select(item => item.Id).ToList();
        var dapperIds = dapperResult.Items.Select(item => item.Id).ToList();
        Assert.Equal(efIds, dapperIds);
        Assert.Equal(2, efIds.Count);
    }

    private void Seed(string author, string text)
    {
        _context.Quotes.Add(Quote.Create(author, text).Value!);
    }

    public void Dispose()
    {
        // Not deleting the file: it lands in bin/<config>/net10.0 (gitignored, like the
        // Data Source=quotes.db dev file and AuthTests.cs's per-test files), and a same-second
        // File.Delete right after Dispose() can race Microsoft.Data.Sqlite's connection pooling
        // on Windows.
        _context.Dispose();
    }
}
