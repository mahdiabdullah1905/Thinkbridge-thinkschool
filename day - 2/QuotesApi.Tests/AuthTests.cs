using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests;

public class AuthTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public AuthTests(TestingWebApplicationFactory factory)
    {
        var dbName = $"Data Source=test_auth_{Guid.NewGuid()}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseSqlite(dbName);
                });
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task AuthenticationFlow_LoginRefreshLogout()
    {
        // 1. Login
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "test@example.com", Password = "password123" });
        loginResp.EnsureSuccessStatusCode();
        var authData = await loginResp.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(authData);
        Assert.NotEmpty(authData.AccessToken);
        Assert.NotEmpty(authData.RefreshToken);

        var token1 = authData.RefreshToken;

        // 2. Refresh
        var refreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = token1 });
        refreshResp.EnsureSuccessStatusCode();
        var refreshedData = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(refreshedData);
        var token2 = refreshedData.RefreshToken;
        Assert.NotEqual(token1, token2);

        // 3. Try to use token1 again (Reuse detection)
        var reuseResp = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = token1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResp.StatusCode);

        // 4. Try to use token2 (Should fail because family was revoked)
        var revokedFamilyResp = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = token2 });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedFamilyResp.StatusCode);

        // 5. Login again for logout test
        var login2Resp = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "test@example.com", Password = "password123" });
        login2Resp.EnsureSuccessStatusCode();
        var authData2 = await login2Resp.Content.ReadFromJsonAsync<AuthResponse>();
        var token3 = authData2!.RefreshToken;

        // 6. Logout
        var logoutResp = await _client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest { RefreshToken = token3 });
        logoutResp.EnsureSuccessStatusCode();

        // 7. Try to use token3 (Should fail because logged out)
        var loggedOutRefreshResp = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = token3 });
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOutRefreshResp.StatusCode);
    }
}
