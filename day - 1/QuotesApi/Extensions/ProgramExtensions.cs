using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Models;
using QuotesApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Extensions;

public static class ProgramExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=quotes.db"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        
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
            var quote = new Quote
            {
                Author = request.Author,
                Text = request.Text
            };

            await repo.AddQuoteAsync(quote, ct);

            return Results.Created($"/api/quotes/{quote.Id}", quote);
        })
        .AddEndpointFilter<ValidationFilter<CreateQuoteRequest>>();

        group.MapGet("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetQuoteByIdAsync(id, ct);
            return quote is not null ? Results.Ok(quote) : Results.NotFound();
        });

        group.MapDelete("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
        {
            var quote = await repo.GetQuoteByIdAsync(id, ct);
            if (quote is null) return Results.NotFound();

            await repo.DeleteQuoteAsync(quote, ct);
            return Results.NoContent();
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

        group.MapPost("/{id}/quotes", async (int id, AddQuoteToCollectionRequest request, ICollectionRepository repo, CancellationToken ct) =>
        {
            var collection = await repo.GetByIdAsync(id, ct);
            if (collection is null) return Results.NotFound();

            try
            {
                collection.AddItem(request.QuoteId);
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
}
