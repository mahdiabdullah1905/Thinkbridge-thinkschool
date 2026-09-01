using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Quotes.Tests.Integration;

public class AuthEndpointsTests
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkAndTokens()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = BCrypt.Net.BCrypt.HashPassword("password");
            db.Users.Add(new User { Email = "test3@example.com", PasswordHash = hash });
            await db.SaveChangesAsync();
        }

        var request = new LoginRequest { Email = "test3@example.com", Password = "password" };
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.NotEmpty(authResponse.AccessToken);
        Assert.NotEmpty(authResponse.RefreshToken);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = BCrypt.Net.BCrypt.HashPassword("password");
            db.Users.Add(new User { Email = "test3@example.com", PasswordHash = hash });
            await db.SaveChangesAsync();
        }

        var request = new LoginRequest { Email = "test3@example.com", Password = "wrongpassword" };
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokens()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        string refreshToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = BCrypt.Net.BCrypt.HashPassword("password");
            var user = new User { Email = "test4@example.com", PasswordHash = hash };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // create a refresh token in DB
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes("valid-refresh-token");
            var tokenHash = Convert.ToBase64String(sha256.ComputeHash(bytes));

            db.RefreshTokens.Add(new RefreshToken
            {
                TokenHash = tokenHash,
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                ExpiresAt = new DateTime(2040, 1, 1)
            });
            await db.SaveChangesAsync();
            refreshToken = "valid-refresh-token";
        }

        var request = new RefreshRequest { RefreshToken = refreshToken };
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.NotEmpty(authResponse.AccessToken);
        Assert.NotEmpty(authResponse.RefreshToken);
    }

    [Fact]
    public async Task Logout_WithValidToken_ReturnsNoContent()
    {
        await using var factory = new CustomWebApplicationFactory();
        var client = factory.CreateClient();

        string refreshToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = BCrypt.Net.BCrypt.HashPassword("password");
            var user = new User { Email = "test5@example.com", PasswordHash = hash };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes("valid-refresh-token-logout");
            var tokenHash = Convert.ToBase64String(sha256.ComputeHash(bytes));

            db.RefreshTokens.Add(new RefreshToken
            {
                TokenHash = tokenHash,
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                ExpiresAt = new DateTime(2040, 1, 1)
            });
            await db.SaveChangesAsync();
            refreshToken = "valid-refresh-token-logout";
        }

        var request = new LogoutRequest { RefreshToken = refreshToken };
        var response = await client.PostAsJsonAsync("/api/auth/logout", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify token is revoked
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(refreshToken);
            var tokenHash = Convert.ToBase64String(sha256.ComputeHash(bytes));
            
            var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash);
            Assert.NotNull(storedToken!.RevokedAt);
        }
    }
}
