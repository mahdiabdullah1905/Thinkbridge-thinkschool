using FluentAssertions;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Unit;

public class QuoteTests
{
    [Fact]
    public void Create_ValidAuthorAndText_ReturnsSuccess()
    {
        // Arrange
        var author = "Alice";
        var text = "This is a quote.";

        // Act
        var result = Quote.Create(author, text);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Author.Should().Be(author);
        result.Value!.Text.Should().Be(text);
        result.Value!.IsDeleted.Should().BeFalse();
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_AuthorNullOrEmpty_ReturnsFailure(string invalidAuthor)
    {
        // Arrange
        var text = "Valid text";

        // Act
        var result = Quote.Create(invalidAuthor, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Author cannot be null or empty.");
    }

    [Fact]
    public void Create_AuthorExceeds200_ReturnsFailure()
    {
        // Arrange
        var author = new string('A', 201);
        var text = "Valid text";

        // Act
        var result = Quote.Create(author, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Author cannot exceed 200 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_TextNullOrEmpty_ReturnsFailure(string invalidText)
    {
        // Arrange
        var author = "Valid Author";

        // Act
        var result = Quote.Create(author, invalidText);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Text cannot be null or empty.");
    }

    [Fact]
    public void Create_TextExceeds1000_ReturnsFailure()
    {
        // Arrange
        var author = "Valid Author";
        var text = new string('T', 1001);

        // Act
        var result = Quote.Create(author, text);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Text cannot exceed 1000 characters.");
    }
}
