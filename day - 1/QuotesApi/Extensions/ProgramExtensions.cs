using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Models;
using QuotesApi.Filters;

namespace QuotesApi.Extensions;

public static class ProgramExtensions
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection") ?? "Data Source=quotes.db"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        
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
}
