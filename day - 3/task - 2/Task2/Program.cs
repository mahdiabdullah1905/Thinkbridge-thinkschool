using QuotesApi.Extensions;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Models;
using QuotesApi.Filters;
using QuotesApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Reuse Day 2 setup (registers SQLite DB and "Bearer" self-issued JWT scheme)
builder.Services.AddInfrastructure(builder.Configuration);

// Register custom authorization handler
builder.Services.AddSingleton<IAuthorizationHandler, Task2.SameAuthorHandler>();

// Add Authorization Policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-edit-quotes", policy => policy.RequireClaim("scope", "quotes.write"))
    .AddPolicy("owner-only", policy => policy.Requirements.Add(new Task2.SameAuthorRequirement()));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

// 2. Reuse Auth and Collection endpoints from Day 2
app.MapAuthEndpoints(builder.Configuration);
app.MapCollectionEndpoints();

// 3. Map Quote Endpoints directly in Task 2 to apply custom policies
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
.RequireAuthorization("can-edit-quotes"); // Requires specific claim

quoteGroup.MapGet("/{id}", async (int id, IQuoteRepository repo, CancellationToken ct) =>
{
    var quote = await repo.GetQuoteByIdAsync(id, ct);
    return quote is not null ? Results.Ok(quote) : Results.NotFound();
});

quoteGroup.MapDelete("/{id}", async (int id, IQuoteRepository repo, IAuthorizationService authService, HttpContext context, CancellationToken ct) =>
{
    var quote = await repo.GetQuoteByIdAsync(id, ct);
    if (quote is null) return Results.NotFound();

    // Resource-based authorization for ownership
    var authResult = await authService.AuthorizeAsync(context.User, quote, "owner-only");
    if (!authResult.Succeeded)
    {
        return Results.Forbid();
    }

    quote.Delete();
    await repo.DeleteQuoteAsync(quote, ct);
    return Results.NoContent();
})
.RequireAuthorization("can-edit-quotes"); // Also requires the base claim to edit

app.Run();
