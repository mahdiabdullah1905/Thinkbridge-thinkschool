using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Task2.Tests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, UrlEncoder encoder) 
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue("X-Test-NoAuth", out _))
        {
            return Task.FromResult(AuthenticateResult.Fail("No auth"));
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };

        if (Request.Headers.TryGetValue("X-Test-Email", out var customEmail))
        {
            claims.RemoveAll(c => c.Type == ClaimTypes.Email);
            claims.Add(new Claim(ClaimTypes.Email, customEmail!));
        }

        if (Request.Headers.TryGetValue("X-Test-Scope", out var scopeValue))
        {
            claims.Add(new Claim("scope", scopeValue!));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
