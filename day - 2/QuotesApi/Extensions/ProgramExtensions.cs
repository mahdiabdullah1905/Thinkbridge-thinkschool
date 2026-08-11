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

namespace QuotesApi.Extensions;

public static class ProgramExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=quotes.db"));

        var jwtKey = configuration["Jwt:Key"] ?? "super_secret_default_development_key_with_at_least_256_bits_length_123456";
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
                };
            });

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

        group.MapPost("/", async (CreateQuoteRequest request, IQuoteRepository repo, CancellationToken ct) =>
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

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, IConfiguration config)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IClock clock, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var jwtKey = config["Jwt:Key"] ?? "super_secret_default_development_key_with_at_least_256_bits_length_123456";
            var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
            
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: clock.UtcNow.UtcDateTime.AddMinutes(15),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256)
            );

            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
            
            user.RefreshToken = Guid.NewGuid().ToString();
            user.RefreshTokenExpiryTime = clock.UtcNow.UtcDateTime.AddDays(7);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = user.RefreshToken,
                ExpiresIn = 15 * 60
            });
        })
        .AddEndpointFilter<ValidationFilter<LoginRequest>>();
    }
}
