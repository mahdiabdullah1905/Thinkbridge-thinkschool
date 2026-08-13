using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Quotes.Tests.Integration;

public class QuoteDto
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class PaginatedResponseDto<T>
{
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = new();
}

[Collection("SharedTestCollection")]
public class QuotesEndpointsTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public QuotesEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<HttpClient> GetAuthClientAsync(CustomWebApplicationFactory factory)
    {
        var client = _client;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = BCrypt.Net.BCrypt.HashPassword("password");
        db.Users.Add(new User { Email = "test2@example.com", PasswordHash = hash });
        await db.SaveChangesAsync();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "test2@example.com", Password = "password" });
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);
        return client;
    }

    [Fact]
    public async Task PostQuote_WithValidDataAndToken_ReturnsCreated()
    {
        var client = await GetAuthClientAsync(_factory);

        var request = new CreateQuoteRequest { Author = "Author", Text = "Valid quote text here." };
        var response = await client.PostAsJsonAsync("/api/quotes", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(quote);
        Assert.Equal("Author", quote.Author);
        
        // Assert DB state
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Quotes.Count());
    }

    [Fact]
    public async Task PostQuote_WithMissingToken_ReturnsUnauthorized()
    {
        var client = _client; // No auth

        var request = new CreateQuoteRequest { Author = "Author", Text = "Valid quote text here." };
        var response = await client.PostAsJsonAsync("/api/quotes", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostQuote_WithInvalidData_ReturnsBadRequest_ProblemDetails()
    {
        var client = await GetAuthClientAsync(_factory);

        var request = new CreateQuoteRequest { Author = "", Text = "" }; // Invalid
        var response = await client.PostAsJsonAsync("/api/quotes", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task GetQuotes_ReturnsPaginatedList()
    {
        var client = _client;

        // Seed some quotes
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Quotes.Add(Quote.Create("Author 1", "Quote 1 text!").Value);
            db.Quotes.Add(Quote.Create("Author 2", "Quote 2 text!").Value);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/quotes?page=1&size=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedResponseDto<QuoteDto>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count());
    }

    [Fact]
    public async Task GetQuoteById_ExistingQuote_ReturnsOk()
    {
        var client = _client;

        int quoteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = Quote.Create("Author 1", "Quote 1 text!").Value;
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            quoteId = quote.Id;
        }

        var response = await client.GetAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(result);
        Assert.Equal(quoteId, result.Id);
    }

    [Fact]
    public async Task GetQuoteById_NonExistingQuote_ReturnsNotFound()
    {
        var client = _client;

        var response = await client.GetAsync("/api/quotes/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_ExistingQuote_ReturnsNoContent()
    {
        var client = await GetAuthClientAsync(_factory);

        int quoteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = Quote.Create("Author 1", "Quote 1 text!").Value;
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            quoteId = quote.Id;
        }

        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quote = await db.Quotes.IgnoreQueryFilters().FirstOrDefaultAsync(q => q.Id == quoteId);
            Assert.NotNull(quote);
            Assert.True(quote.IsDeleted);
        }
    }

    [Fact]
    public async Task DeleteQuote_NonExistingQuote_ReturnsNotFound()
    {
        var client = await GetAuthClientAsync(_factory);

        var response = await client.DeleteAsync("/api/quotes/999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_WithMissingToken_ReturnsUnauthorized()
    {
        var client = _client;

        var response = await client.DeleteAsync("/api/quotes/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostQuote_WithInvalidToken_ReturnsUnauthorized()
    {
        var client = _client;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var request = new CreateQuoteRequest { Author = "Author", Text = "Valid quote text here." };
        var response = await client.PostAsJsonAsync("/api/quotes", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

