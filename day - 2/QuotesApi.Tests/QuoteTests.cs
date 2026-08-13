using QuotesApi.Models;

namespace QuotesApi.Tests;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidData_ReturnsSuccessResultAndQuote()
    {
        // Arrange
        var author = "Steve Jobs";
        var text = "Stay hungry, stay foolish.";

        // Act
        var result = Quote.Create(author, text);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(author, result.Value.Author);
        Assert.Equal(text, result.Value.Text);
        Assert.False(result.Value.IsDeleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidAuthor_ReturnsFailedResult(string? invalidAuthor)
    {
        // Act
        var result = Quote.Create(invalidAuthor!, "Valid text");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public void Create_WithAuthorExceeding200Chars_ReturnsFailedResult()
    {
        // Arrange
        var longAuthor = new string('A', 201);

        // Act
        var result = Quote.Create(longAuthor, "Valid text");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidText_ReturnsFailedResult(string? invalidText)
    {
        // Act
        var result = Quote.Create("Valid Author", invalidText!);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public void Create_WithTextExceeding1000Chars_ReturnsFailedResult()
    {
        // Arrange
        var longText = new string('T', 1001);

        // Act
        var result = Quote.Create("Valid Author", longText);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Delete_SetsIsDeletedToTrue()
    {
        // Arrange
        var quote = Quote.Create("Author", "Text").Value!;

        // Act
        quote.Delete();

        // Assert
        Assert.True(quote.IsDeleted);
    }
}
