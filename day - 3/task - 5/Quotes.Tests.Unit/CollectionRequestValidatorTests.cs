using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Unit;

public class CollectionRequestValidatorTests
{
    private static bool ValidateModel(object model, out List<ValidationResult> results)
    {
        var context = new ValidationContext(model);
        results = new List<ValidationResult>();
        return Validator.TryValidateObject(model, context, results, true);
    }

    [Fact]
    public void CreateCollectionRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new CreateCollectionRequest { Name = "My Favorites", OwnerId = "alice@example.com" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ab")]
    public void CreateCollectionRequest_NameTooShort_FailsValidation(string invalidName)
    {
        // Arrange
        var request = new CreateCollectionRequest { Name = invalidName, OwnerId = "alice@example.com" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateCollectionRequest.Name)));
    }

    [Fact]
    public void CreateCollectionRequest_NameTooLong_FailsValidation()
    {
        // Arrange
        var request = new CreateCollectionRequest { Name = new string('N', 81), OwnerId = "alice@example.com" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateCollectionRequest.Name)));
    }

    [Fact]
    public void CreateCollectionRequest_MissingOwnerId_FailsValidation()
    {
        // Arrange
        var request = new CreateCollectionRequest { Name = "Valid Name", OwnerId = null! };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateCollectionRequest.OwnerId)));
    }

    [Fact]
    public void AddQuoteToCollectionRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new AddQuoteToCollectionRequest { QuoteId = 123 };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
