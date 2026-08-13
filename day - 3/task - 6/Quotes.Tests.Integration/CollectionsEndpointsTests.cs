using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Quotes.Tests.Integration;

public class CollectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
}

public class CollectionsEndpointsTests
{
    private async Task<HttpClient> GetAuthClientAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
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
    public async Task PostCollection_WithValidData_ReturnsCreated()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = await GetAuthClientAsync(factory);

        var request = new CreateCollectionRequest { Name = "My Favorites", OwnerId = "1" };
        var response = await client.PostAsJsonAsync("/api/collections", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var collection = await response.Content.ReadFromJsonAsync<CollectionDto>();
        Assert.NotNull(collection);
        Assert.Equal("My Favorites", collection.Name);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, db.Collections.Count());
    }

    [Fact]
    public async Task AddQuoteToCollection_WithValidData_ReturnsOk()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        int collectionId;
        int quoteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collection = new Collection("Favs", "1");
            db.Collections.Add(collection);
            var quote = Quote.Create("Author", "Text text").Value;
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
            quoteId = quote.Id;
        }

        var request = new AddQuoteToCollectionRequest { QuoteId = quoteId };
        var response = await client.PostAsJsonAsync($"/api/collections/{collectionId}/quotes", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var coll = await db.Collections.FindAsync(collectionId);
            Assert.Single(coll!.Items);
            Assert.Equal(quoteId, coll.Items.First().QuoteId);
        }
    }

    [Fact]
    public async Task PostCollection_WithInvalidData_ReturnsBadRequest_ProblemDetails()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new CreateCollectionRequest { Name = "", OwnerId = "" };
        var response = await client.PostAsJsonAsync("/api/collections", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task AddQuoteToCollection_WithInvalidData_ReturnsBadRequest_ProblemDetails()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        int collectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collection = new Collection("Favs", "1");
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
        }

        var request = new AddQuoteToCollectionRequest { QuoteId = -1 };
        var response = await client.PostAsJsonAsync($"/api/collections/{collectionId}/quotes", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemoveQuoteFromCollection_WithValidData_ReturnsNoContent()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        int collectionId;
        int quoteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collection = new Collection("Favs", "1");
            db.Collections.Add(collection);
            var quote = Quote.Create("Author", "Text text").Value;
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            collection.AddItem(quote.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
            quoteId = quote.Id;
        }

        var response = await client.DeleteAsync($"/api/collections/{collectionId}/quotes/{quoteId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveQuoteFromCollection_NonExistingQuote_ReturnsNotFound_ProblemDetails()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        int collectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var collection = new Collection("Favs", "1");
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            collectionId = collection.Id;
        }

        var response = await client.DeleteAsync($"/api/collections/{collectionId}/quotes/999");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("An error occurred while processing your request.", problem.Title);
    }
}
