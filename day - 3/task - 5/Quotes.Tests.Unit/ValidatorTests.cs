using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Unit;

public class ValidatorTests
{
    private static bool ValidateModel(object model, out List<ValidationResult> results)
    {
        var context = new ValidationContext(model);
        results = new List<ValidationResult>();
        return Validator.TryValidateObject(model, context, results, true);
    }

    [Fact]
    public void CreateQuoteRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = "Alice", Text = "Valid Text" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void CreateQuoteRequest_MissingAuthor_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = null!, Text = "Valid Text" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Author)));
    }

    [Fact]
    public void CreateQuoteRequest_AuthorTooShort_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = "", Text = "Valid Text" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Author)));
    }

    [Fact]
    public void CreateQuoteRequest_AuthorTooLong_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = new string('A', 101), Text = "Valid Text" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Author)));
    }

    [Fact]
    public void CreateQuoteRequest_MissingText_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = "Alice", Text = null! };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Text)));
    }

    [Fact]
    public void CreateQuoteRequest_TextTooShort_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = "Alice", Text = "" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Text)));
    }

    [Fact]
    public void CreateQuoteRequest_TextTooLong_FailsValidation()
    {
        // Arrange
        var request = new CreateQuoteRequest { Author = "Alice", Text = new string('T', 1001) };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateQuoteRequest.Text)));
    }

    [Fact]
    public void LoginRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new LoginRequest { Email = "alice@example.com", Password = "Password123!" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void LoginRequest_MissingEmail_FailsValidation()
    {
        // Arrange
        var request = new LoginRequest { Email = null!, Password = "Password123!" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequest.Email)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    [InlineData("alice@")]
    public void LoginRequest_InvalidEmail_FailsValidation(string invalidEmail)
    {
        // Arrange
        var request = new LoginRequest { Email = invalidEmail, Password = "Password123!" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequest.Email)));
    }

    [Fact]
    public void LoginRequest_MissingPassword_FailsValidation()
    {
        // Arrange
        var request = new LoginRequest { Email = "alice@example.com", Password = null! };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequest.Password)));
    }

    [Fact]
    public void RefreshRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new RefreshRequest { RefreshToken = "valid-token" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void RefreshRequest_MissingToken_FailsValidation()
    {
        // Arrange
        var request = new RefreshRequest { RefreshToken = null! };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RefreshRequest.RefreshToken)));
    }
    
    [Fact]
    public void LogoutRequest_Valid_PassesValidation()
    {
        // Arrange
        var request = new LogoutRequest { RefreshToken = "valid-token" };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void LogoutRequest_MissingToken_FailsValidation()
    {
        // Arrange
        var request = new LogoutRequest { RefreshToken = null! };

        // Act
        var isValid = ValidateModel(request, out var results);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LogoutRequest.RefreshToken)));
    }
}
