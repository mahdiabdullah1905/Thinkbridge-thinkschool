using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=quotes.db"));

var app = builder.Build();

app.MapGet("/api/quotes", async (AppDbContext db) =>
{
    return await db.Quotes.ToListAsync();
});

app.MapPost("/api/quotes", async (Quote quote, AppDbContext db) =>
{
    db.Quotes.Add(quote);
    await db.SaveChangesAsync();

    return Results.Created($"/api/quotes/{quote.Id}", quote);
});

app.MapGet("/api/quotes/{id}", async (int id, AppDbContext db) =>
{
    var quote = await db.Quotes.FindAsync(id);

    if (quote == null)
        return Results.NotFound();

    return Results.Ok(quote);
});

app.MapDelete("/api/quotes/{id}", async (int id, AppDbContext db) =>
{
    var quote = await db.Quotes.FindAsync(id);

    if (quote == null)
        return Results.NotFound();

    db.Quotes.Remove(quote);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();