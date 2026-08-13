using QuotesApi.Extensions;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Models;
using QuotesApi.Filters;
using QuotesApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Task3;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(defaultScheme: "SmartPolicy")
    .AddPolicyScheme("SmartPolicy", "Smart Policy", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    if (jwt.Issuer.Contains("login.microsoftonline.com") || jwt.Issuer.Contains("sts.windows.net"))
                    {
                        return "Entra";
                    }
                }
            }
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer("Entra", options =>
    {
        var tenantId = builder.Configuration["Entra:TenantId"];
        var audience = builder.Configuration["Entra:Audience"];
        
        if (!string.IsNullOrEmpty(tenantId))
        {
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuers = new[] 
                { 
                    $"https://login.microsoftonline.com/{tenantId}/v2.0", 
                    $"https://sts.windows.net/{tenantId}/" 
                },
                ValidAudience = audience
            };
        }
        options.Audience = audience;
    });

builder.Services.AddTransient<IClaimsTransformation, InternalUserClaimsTransformation>();

builder.Services.AddSingleton<IAuthorizationHandler, SameAuthorHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-edit-quotes", policy => policy.RequireClaim("scope", "quotes.write"))
    .AddPolicy("owner-only", policy => policy.Requirements.Add(new SameAuthorRequirement()));

var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints(builder.Configuration);
app.MapCollectionEndpoints();

var quoteGroup = app.MapGroup("/api/quotes");

quoteGroup.MapGet("/", async (IQuoteRepository repo, int? page, int? size, CancellationToken ct) =>
{
    var p = page.HasValue && page.Value >= 1 ? page.Value : 1;
    var s = size.HasValue && size.Value >= 1 && size.Value <= 100 ? size.Value : 10;
    var (quotes, totalCount) = await repo.GetQuotesAsync(p, s, ct);
    return Results.Ok(new PaginatedResponse<Quote>
    {
        Page = p,
        Size = s,
        TotalCount = totalCount,
        Items = quotes
    });
});

quoteGroup.MapPost("/", async (CreateQuoteRequest request, IQuoteRepository repo, CancellationToken ct) =>
{
    var result = Quote.Create(request.Author, request.Text);
    if (!result.IsSuccess)
    {
        return Results.BadRequest(new ProblemDetails { Title = "Invalid Quote", Detail = result.Error });
    }
    var quote = result.Value!;
    await repo.AddQuoteAsync(quote, ct);
    return Results.Created($"/api/quotes/{quote.Id}", quote);
})
.AddEndpointFilter<ValidationFilter<CreateQuoteRequest>>()
.RequireAuthorization("can-edit-quotes");

quoteGroup.MapGet("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
{
    var quote = await repo.GetQuoteByIdAsync(id, ct);
    return quote is not null ? Results.Ok(quote) : Results.NotFound();
});

quoteGroup.MapDelete("/{id}", async (int id, IQuoteRepository repo, IAuthorizationService authService, HttpContext context, CancellationToken ct) =>
{
    var quote = await repo.GetQuoteByIdAsync(id, ct);
    if (quote is null) return Results.NotFound();

    var authResult = await authService.AuthorizeAsync(context.User, quote, "owner-only");
    if (!authResult.Succeeded)
    {
        return Results.Forbid();
    }

    quote.Delete();
    await repo.DeleteQuoteAsync(quote, ct);
    return Results.NoContent();
})
.RequireAuthorization("can-edit-quotes");

app.Run();

namespace Task3
{
    public class InternalUserClaimsTransformation : IClaimsTransformation
    {
        private readonly IConfiguration _config;
        public InternalUserClaimsTransformation(IConfiguration config) => _config = config;

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var expectedIssuer = _config["Jwt:Issuer"];
            if (principal.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
            {
                if (identity.HasClaim(c => c.Issuer == expectedIssuer))
                {
                    if (!principal.HasClaim("scope", "quotes.write"))
                    {
                        var clone = principal.Clone();
                        var newIdentity = (ClaimsIdentity)clone.Identity!;
                        newIdentity.AddClaim(new Claim("scope", "quotes.write"));
                        return Task.FromResult(clone);
                    }
                }
            }
            return Task.FromResult(principal);
        }
    }
}
