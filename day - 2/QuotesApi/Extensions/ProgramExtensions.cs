using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Models;
using QuotesApi.Filters;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Services;
using QuotesApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Diagnostics;
using QuotesApi.Configuration;
using Microsoft.Extensions.Options;

namespace QuotesApi.Extensions;

public static class ProgramExtensions
{
    private static readonly ActivitySource ActivitySource = new("QuotesApi");

    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=quotes.db"));

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) =>
            {
                var jwtOptions = jwt.Value;
                if (string.IsNullOrEmpty(jwtOptions.Key))
                {
                    throw new InvalidOperationException("JWT configuration is missing or invalid. Check User Secrets.");
                }

                var keyBytes = Encoding.UTF8.GetBytes(jwtOptions.Key);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
                };
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization();

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        
        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<ExceptionHandlingMiddleware>();
        
        services.AddProblemDetails(); // Built-in support for ProblemDetails
    }

    public static void MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("/", async (IQuoteRepository repo, int? page, int? size, CancellationToken ct) =>
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

        group.MapPost("/", async (CreateQuoteRequest request, IQuoteRepository repo, ILogger<Program> logger, CancellationToken ct) =>
        {
            using var activity = ActivitySource.StartActivity("CreateQuote");

            logger.LogInformation("Received request to create a quote for author {Author}", request.Author);

            var result = Quote.Create(request.Author, request.Text);
            if (!result.IsSuccess)
            {
                logger.LogWarning("Quote validation failed for author {Author}: {Error}", request.Author, result.Error);
                return Results.BadRequest(new ProblemDetails { Title = "Invalid Quote", Detail = result.Error });
            }

            var quote = result.Value!;
            logger.LogInformation("Successfully instantiated quote with ID {QuoteId}", quote.Id);

            logger.LogInformation("Saving quote {QuoteId} to the database", quote.Id);
            await repo.AddQuoteAsync(quote, ct);

            logger.LogInformation("Successfully saved quote {QuoteId}", quote.Id);

            logger.LogInformation("Returning Created response for quote {QuoteId}", quote.Id);
            return Results.Created($"/api/quotes/{quote.Id}", quote);
        })
        .AddEndpointFilter<ValidationFilter<CreateQuoteRequest>>()
        .RequireAuthorization();

        group.MapGet("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetQuoteByIdAsync(id, ct);
            return quote is not null ? Results.Ok(quote) : Results.NotFound();
        });

        group.MapDelete("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetQuoteByIdAsync(id, ct);
            if (quote is null) return Results.NotFound();

            quote.Delete();
            await repo.DeleteQuoteAsync(quote, ct);
            return Results.NoContent();
        })
        .RequireAuthorization();
    }

    public static void MapAuthorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authors");

        // Author directory: every author together with their quotes. Written the quick way -
        // one query for the distinct author names, then one more query per author - instead of
        // a single grouped query, so it does N+1 round trips against a Quotes.Author column that
        // has no index to back the per-author WHERE clause.
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var authors = await db.Quotes
                .Select(q => q.Author)
                .Distinct()
                .ToListAsync(ct);

            var summaries = new List<AuthorSummary>();

            foreach (var author in authors)
            {
                var quotesForAuthor = await db.Quotes
                    .Where(q => q.Author == author)
                    .ToListAsync(ct);

                summaries.Add(new AuthorSummary(author, quotesForAuthor.Count, quotesForAuthor.Select(q => q.Text).ToList()));
            }

            return Results.Ok(summaries);
        });
    }

    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections");

        group.MapPost("/", async (CreateCollectionRequest request, ICollectionRepository repo, CancellationToken ct) =>
        {
            try
            {
                var collection = new Collection(request.Name, request.OwnerId);
                await repo.AddAsync(collection, ct);
                return Results.Created($"/api/collections/{collection.Id}", collection);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ProblemDetails { Title = "Invalid Collection", Detail = ex.Message });
            }
        })
        .AddEndpointFilter<ValidationFilter<CreateCollectionRequest>>();

        group.MapPost("/{id}/quotes", async (int id, AddQuoteToCollectionRequest request, ICollectionRepository repo, IClock clock, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();

            try
            {
                collection.AddItem(request.QuoteId, clock.UtcNow);
                await repo.UpdateAsync(collection, ct);
                return Results.Ok(collection);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ProblemDetails { Title = "Cannot add quote", Detail = ex.Message });
            }
        })
        .AddEndpointFilter<ValidationFilter<AddQuoteToCollectionRequest>>();

        group.MapDelete("/{id}/quotes/{quoteId}", async (int id, int quoteId, ICollectionRepository repo, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();

            if (!collection.RemoveItem(quoteId))
            {
                return Results.NotFound(new ProblemDetails { Title = "Not Found", Detail = $"Quote {quoteId} is not in the collection." });
            }

            await repo.UpdateAsync(collection, ct);
            return Results.NoContent();
        });
    }

    private static string GenerateSecureToken()
    {
        var randomBytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return Convert.ToBase64String(randomBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string GenerateJwt(User user, IOptionsSnapshot<JwtOptions> jwtOptionsMonitor, IClock clock)
    {
        var jwtOptions = jwtOptionsMonitor.Value;
        var keyBytes = Encoding.UTF8.GetBytes(jwtOptions.Key);
        
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            expires: clock.UtcNow.UtcDateTime.AddMinutes(15),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IClock clock, IOptionsSnapshot<JwtOptions> jwtOptions, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var accessToken = GenerateJwt(user, jwtOptions, clock);
            var rawRefreshToken = GenerateSecureToken();
            var refreshTokenHash = HashToken(rawRefreshToken);

            var refreshTokenRecord = new RefreshToken
            {
                TokenHash = refreshTokenHash,
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                ExpiresAt = clock.UtcNow.UtcDateTime.AddDays(7)
            };

            db.RefreshTokens.Add(refreshTokenRecord);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                ExpiresIn = 15 * 60
            });
        })
        .AddEndpointFilter<ValidationFilter<LoginRequest>>();

        group.MapPost("/refresh", async (RefreshRequest request, AppDbContext db, IClock clock, ILogger<Program> logger, IOptionsSnapshot<JwtOptions> jwtOptions, CancellationToken ct) =>
        {
            var tokenHash = HashToken(request.RefreshToken);
            var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

            if (storedToken is null)
            {
                return Results.Unauthorized();
            }

            // Check if the token was already used in a rotation or revoked
            if (storedToken.RevokedAt is not null || storedToken.ReplacedByTokenHash is not null)
            {
                if (storedToken.ReplacedByTokenHash is not null)
                {
                    // This is a reuse/theft attempt during rotation!
                    logger.LogWarning("Security Event: Refresh token reuse detected for family {FamilyId}. Entire token family revoked.", storedToken.FamilyId);
                    
                    var familyTokens = await db.RefreshTokens
                        .Where(r => r.FamilyId == storedToken.FamilyId && r.RevokedAt == null)
                        .ToListAsync(ct);
                        
                    foreach (var token in familyTokens)
                    {
                        token.RevokedAt = clock.UtcNow.UtcDateTime;
                    }
                    await db.SaveChangesAsync(ct);
                }
                
                return Results.Unauthorized();
            }

            if (storedToken.ExpiresAt < clock.UtcNow.UtcDateTime)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == storedToken.UserId, ct);
            if (user is null) return Results.Unauthorized();

            var newAccessToken = GenerateJwt(user, jwtOptions, clock);
            var newRawRefreshToken = GenerateSecureToken();
            var newRefreshTokenHash = HashToken(newRawRefreshToken);

            storedToken.RevokedAt = clock.UtcNow.UtcDateTime;
            storedToken.ReplacedByTokenHash = newRefreshTokenHash;

            var newRefreshTokenRecord = new RefreshToken
            {
                TokenHash = newRefreshTokenHash,
                UserId = user.Id,
                FamilyId = storedToken.FamilyId,
                ExpiresAt = clock.UtcNow.UtcDateTime.AddDays(7)
            };

            db.RefreshTokens.Add(newRefreshTokenRecord);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AuthResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRawRefreshToken,
                ExpiresIn = 15 * 60
            });
        })
        .AddEndpointFilter<ValidationFilter<RefreshRequest>>();

        group.MapPost("/logout", async (LogoutRequest request, AppDbContext db, IClock clock, CancellationToken ct) =>
        {
            var tokenHash = HashToken(request.RefreshToken);
            var storedToken = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

            if (storedToken is not null && storedToken.RevokedAt is null)
            {
                storedToken.RevokedAt = clock.UtcNow.UtcDateTime;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        })
        .AddEndpointFilter<ValidationFilter<LogoutRequest>>();
    }
}
