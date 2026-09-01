using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Commands;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Tests;

public class CreateQuoteCommandHandlerTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly CreateQuoteCommandHandler _handler;

    public CreateQuoteCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var repository = new QuoteRepository(_context, new NullLogger<QuoteRepository>());
        _handler = new CreateQuoteCommandHandler(repository, new NullLogger<CreateQuoteCommandHandler>());
    }

    [Fact]
    public async Task Handle_WithValidCommand_PersistsQuoteAndReturnsIt()
    {
        // Act
        var result = await _handler.Handle(new CreateQuoteCommand("Ada Lovelace", "The Analytical Engine weaves algebra."), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(0, result.Value.Id);

        var stored = await _context.Quotes.FirstOrDefaultAsync(q => q.Id == result.Value.Id);
        Assert.NotNull(stored);
        Assert.Equal("Ada Lovelace", stored.Author);
    }

    [Fact]
    public async Task Handle_WithEmptyAuthor_ReturnsFailureAndDoesNotPersist()
    {
        // Act
        var result = await _handler.Handle(new CreateQuoteCommand("", "Some text"), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Empty(await _context.Quotes.ToListAsync());
    }

    [Fact]
    public async Task Handle_WithTextTooLong_ReturnsFailureAndDoesNotPersist()
    {
        // Act
        var result = await _handler.Handle(new CreateQuoteCommand("Author", new string('x', 1001)), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Empty(await _context.Quotes.ToListAsync());
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
