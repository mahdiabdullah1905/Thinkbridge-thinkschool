using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Task3;

namespace Task3.Tests;

public record TestQuote(int Id, string Author, string Text);
public record TestPaginatedResponse(int Page, int Size, int TotalCount, List<TestQuote> Items);

public class IntegrationTests : IDisposable
{
    private readonly WebApplicationFactory<Task3Marker> _factory;
    private readonly SqliteConnection _connection;
    private readonly FakeClock _fakeClock;

    public IntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _fakeClock = new FakeClock();

        _factory = new WebApplicationFactory<Task3Marker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                
                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseSqlite(_connection);
                });

                var clockDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IClock));
                if (clockDescriptor != null) services.Remove(clockDescriptor);
                
                services.AddSingleton<IClock>(_fakeClock);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        db.Users.Add(new User { Email = "user1@example.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123") });
        db.Users.Add(new User { Email = "user2@example.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123") });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Close();
        _factory.Dispose();
    }

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "password123" });
        response.EnsureSuccessStatusCode();
        var authResult = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return (authResult!.AccessToken, authResult.RefreshToken);
    }

    [Fact]
    public async Task Scenario1_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "Anon", Text = "Should fail" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Scenario2_WrongPolicy_Returns403()
    {
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var (token1, _) = await LoginAsync(client1, "user1@example.com");
        var (token2, _) = await LoginAsync(client2, "user2@example.com");

        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var createResp = await client1.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "My quote" });
        createResp.EnsureSuccessStatusCode();
        var quote = await createResp.Content.ReadFromJsonAsync<TestQuote>();

        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var deleteResp = await client2.DeleteAsync($"/api/quotes/{quote!.Id}");
        
        deleteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scenario3_RightPolicy_Returns204()
    {
        var client = _factory.CreateClient();
        var (token, _) = await LoginAsync(client, "user1@example.com");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResp = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "My quote" });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var quote = await createResp.Content.ReadFromJsonAsync<TestQuote>();

        var deleteResp = await client.DeleteAsync($"/api/quotes/{quote!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Scenario4_ExpiredToken_Returns401()
    {
        _fakeClock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(-20);
        var client = _factory.CreateClient();
        var (token, _) = await LoginAsync(client, "user1@example.com");

        _fakeClock.UtcNow = DateTimeOffset.UtcNow;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "Test" });
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Scenario5_RevokedRefreshChain_Returns401()
    {
        var client = _factory.CreateClient();
        
        var (_, refreshTokenA) = await LoginAsync(client, "user1@example.com");
        
        var refreshResp1 = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = refreshTokenA });
        refreshResp1.EnsureSuccessStatusCode();
        var authResult1 = await refreshResp1.Content.ReadFromJsonAsync<AuthResponse>();
        var refreshTokenB = authResult1!.RefreshToken;

        var reuseResp = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = refreshTokenA });
        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var refreshResp2 = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = refreshTokenB });
        refreshResp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task Scenario6_GetQuotes_ReturnsPaginatedList()
    {
        var client = _factory.CreateClient();
        var (token, _) = await LoginAsync(client, "user1@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Seed a quote
        var createResp = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "GetQuotes test" });
        createResp.EnsureSuccessStatusCode();

        // Call GET /api/quotes
        var getResp = await client.GetAsync("/api/quotes");
        getResp.EnsureSuccessStatusCode();

        var page = await getResp.Content.ReadFromJsonAsync<TestPaginatedResponse>();
        page.Should().NotBeNull();
        page!.Items.Count.Should().BeGreaterThan(0);
        page.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Scenario7_GetQuoteById_ReturnsQuote()
    {
        var client = _factory.CreateClient();
        var (token, _) = await LoginAsync(client, "user1@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Seed a quote
        var createResp = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "GetById test" });
        createResp.EnsureSuccessStatusCode();
        var createdQuote = await createResp.Content.ReadFromJsonAsync<TestQuote>();

        // Call GET /api/quotes/{id}
        var getResp = await client.GetAsync($"/api/quotes/{createdQuote!.Id}");
        getResp.EnsureSuccessStatusCode();
        
        var retrievedQuote = await getResp.Content.ReadFromJsonAsync<TestQuote>();
        retrievedQuote.Should().NotBeNull();
        retrievedQuote!.Text.Should().Be("GetById test");
    }

    [Fact]
    public async Task Scenario8_PostQuote_InvalidData_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var (token, _) = await LoginAsync(client, "user1@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // POST invalid data (empty Text)
        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest { Author = "user1@example.com", Text = "" });
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
    }
}
