using System.Net;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace quotes_bff;

// The only new piece of compute in the Day 17 Managed Identity architecture. This is a thin
// reverse proxy: the browser never talks to quotes-api directly, it talks to this Function
// (reached only through the SWA's linked-backend trust, same mechanism verified in the earlier
// Day 17 work). This Function's system-assigned identity requests an Entra ID token for
// quotes-api's own Application ID URI - no client secret is read, stored, or referenced anywhere
// in this file or in the Function App's configuration.
public class QuotesApiProxy
{
    // Reused across invocations. DefaultAzureCredential resolves to ManagedIdentityCredential
    // automatically once running on real Azure compute (falls back to az-login locally, which is
    // fine since a real Managed Identity token can only ever be obtained when actually deployed).
    private static readonly HttpClient Http = new();
    private static readonly DefaultAzureCredential Credential = new();

    private readonly ILogger<QuotesApiProxy> _logger;

    public QuotesApiProxy(ILogger<QuotesApiProxy> logger)
    {
        _logger = logger;
    }

    [Function("QuotesApiProxy")]
    public async Task Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "delete", Route = "{*path}")] HttpRequest req,
        string path,
        CancellationToken ct)
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable("QuotesApi__BaseUrl")
            ?? throw new InvalidOperationException("QuotesApi__BaseUrl app setting is not configured.");
        var apiAppIdUri = Environment.GetEnvironmentVariable("QuotesApi__AppIdUri")
            ?? throw new InvalidOperationException("QuotesApi__AppIdUri app setting is not configured.");

        AccessToken token;
        try
        {
            token = await Credential.GetTokenAsync(
                new TokenRequestContext([$"{apiAppIdUri}/.default"]), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire a Managed Identity token for audience {AppIdUri}", apiAppIdUri);
            req.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await req.HttpContext.Response.WriteAsync("BFF could not acquire a Managed Identity token.", ct);
            return;
        }

        // Proof this really is a Managed-Identity-obtained token, without ever exposing the raw
        // token itself: decode only the non-secret claims that matter for verification (audience,
        // issuer, the calling app/identity, expiry) and surface them as response headers below.
        var claims = DecodeTokenClaims(token.Token, apiAppIdUri);

        var targetUri = new Uri($"{apiBaseUrl.TrimEnd('/')}/api/{path}{req.QueryString}");
        using var outbound = new HttpRequestMessage(new HttpMethod(req.Method), targetUri);

        if (req.Method is "POST" or "PUT" or "PATCH")
        {
            outbound.Content = new StreamContent(req.Body);
            if (req.ContentType is not null)
            {
                outbound.Content.Headers.TryAddWithoutValidation("Content-Type", req.ContentType);
            }
        }

        // Layer 1 (this Function -> quotes-api trust): the Managed-Identity-acquired Entra token,
        // presented in the standard Authorization header so quotes-api's Entra validation can
        // check it.
        outbound.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        // Layer 2 (end-user authorization, unchanged): the original caller's existing HS256 JWT is
        // forwarded verbatim, just relocated to a header the Authorization slot is no longer free
        // for on this hop. quotes-api reads this via an additive fallback - its own
        // Authorization-header validation logic is untouched for every other caller.
        if (req.Headers.TryGetValue("Authorization", out var userAuth) && userAuth.Count > 0)
        {
            outbound.Headers.TryAddWithoutValidation("X-User-Token", userAuth.ToString());
        }

        using var response = await Http.SendAsync(outbound, ct);

        req.HttpContext.Response.StatusCode = (int)response.StatusCode;
        if (response.Content.Headers.ContentType is not null)
        {
            req.HttpContext.Response.ContentType = response.Content.Headers.ContentType.ToString();
        }
        // Diagnostic headers only - confirm which trust layer handled the request and prove the
        // token really came from Managed Identity, without ever exposing the token itself.
        req.HttpContext.Response.Headers["X-Bff-Auth"] = "managed-identity";
        req.HttpContext.Response.Headers["X-Mi-Token-Aud"] = claims.Audience ?? "(decode-failed)";
        req.HttpContext.Response.Headers["X-Mi-Token-Iss"] = claims.Issuer ?? "(decode-failed)";
        req.HttpContext.Response.Headers["X-Mi-Token-Appid"] = claims.AppId ?? "(decode-failed)";
        req.HttpContext.Response.Headers["X-Mi-Token-Exp"] = claims.Expiry?.ToString("O") ?? "(decode-failed)";

        await response.Content.CopyToAsync(req.HttpContext.Response.Body, ct);
    }

    private readonly record struct TokenClaims(string? Audience, string? Issuer, string? AppId, DateTimeOffset? Expiry);

    // Deliberately no JWT library dependency here - just decode the middle (payload) segment of
    // the JWT ourselves. This is for logging/verification only (proving the token's aud/iss/appid
    // claims), never for trusting the token - quotes-api is the one that actually validates it.
    private TokenClaims DecodeTokenClaims(string jwt, string expectedAudience)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                _logger.LogWarning("Acquired token did not look like a JWT (unexpected part count)");
                return default;
            }

            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var aud = root.TryGetProperty("aud", out var audEl) ? audEl.GetString() : null;
            var iss = root.TryGetProperty("iss", out var issEl) ? issEl.GetString() : null;
            var appid = root.TryGetProperty("appid", out var appidEl) ? appidEl.GetString()
                : root.TryGetProperty("azp", out var azpEl) ? azpEl.GetString() : null;
            var exp = root.TryGetProperty("exp", out var expEl) ? expEl.GetInt64() : 0L;

            _logger.LogInformation(
                "Managed Identity token acquired. aud={Audience} (expected {Expected}) iss={Issuer} appid={AppId} exp={Expiry}",
                aud, expectedAudience, iss, appid, DateTimeOffset.FromUnixTimeSeconds(exp));

            return new TokenClaims(aud, iss, appid, DateTimeOffset.FromUnixTimeSeconds(exp));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not decode acquired token for logging (non-fatal, request still proceeds)");
            return default;
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
