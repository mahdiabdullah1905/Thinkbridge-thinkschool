using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using QuotesApi.Models;

namespace Task2.Tests;

public class AuthPolicyTests : IClassFixture<WebApplicationFactory<Task2.Task2Marker>>
{
    private readonly WebApplicationFactory<Task2.Task2Marker> _factory;

    public AuthPolicyTests(WebApplicationFactory<Task2.Task2Marker> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("TestScheme")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });
                services.AddAuthorization(options =>
                {
                    // Fallback to TestScheme for testing instead of JWT
                    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("TestScheme")
                        .RequireAuthenticatedUser()
                        .Build();
                });
            });
        });
    }

    [Fact]
    public async Task Unauthenticated_User_Returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-NoAuth", "true");

        var response = await client.PostAsJsonAsync("/api/quotes", new { Author = "Alice", Text = "Hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_Without_Claim_Returns_403()
    {
        var client = _factory.CreateClient();
        // Uses default test user, which lacks 'scope=quotes.write'

        var response = await client.PostAsJsonAsync("/api/quotes", new { Author = "Alice", Text = "Hello" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_With_Required_Claim_Succeeds()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scope", "quotes.write");

        var response = await client.PostAsJsonAsync("/api/quotes", new { Author = "test@example.com", Text = "Hello" });

        // Depending on validation, 201 Created is expected
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public record QuoteDto(int Id, string Author, string Text);

    [Fact]
    public async Task Custom_Authorization_Requirement_Fails_When_Not_Author()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scope", "quotes.write");
        client.DefaultRequestHeaders.Add("X-Test-Email", "bob@example.com");

        // Create a quote as Alice
        var createClient = _factory.CreateClient();
        createClient.DefaultRequestHeaders.Add("X-Test-Scope", "quotes.write");
        createClient.DefaultRequestHeaders.Add("X-Test-Email", "alice@example.com");
        var createResponse = await createClient.PostAsJsonAsync("/api/quotes", new { Author = "alice@example.com", Text = "Alice's Quote" });
        createResponse.EnsureSuccessStatusCode();
        var quote = await createResponse.Content.ReadFromJsonAsync<QuoteDto>();

        // Bob tries to delete Alice's quote
        var deleteResponse = await client.DeleteAsync($"/api/quotes/{quote!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Custom_Authorization_Requirement_Succeeds_When_Author()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scope", "quotes.write");
        client.DefaultRequestHeaders.Add("X-Test-Email", "alice@example.com");

        // Alice creates a quote
        var createResponse = await client.PostAsJsonAsync("/api/quotes", new { Author = "alice@example.com", Text = "Alice's Quote" });
        createResponse.EnsureSuccessStatusCode();
        var quote = await createResponse.Content.ReadFromJsonAsync<QuoteDto>();

        // Alice tries to delete her own quote
        var deleteResponse = await client.DeleteAsync($"/api/quotes/{quote!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
