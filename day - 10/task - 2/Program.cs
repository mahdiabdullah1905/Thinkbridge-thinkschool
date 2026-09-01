using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuotesApi.Data;
using QuotesApi.Models;

const string ConnectionString = "Data Source=day10-task2.db";
const int RowCount = 2_000;

// LogTo + EnableSensitiveDataLogging are only ever turned on for the DbContext instances
// created here, for this console demo. The real QuotesApi app (day - 2/QuotesApi) is
// untouched and never gets sensitive-data logging enabled.
AppDbContext NewContext(bool logSql = false, bool sensitiveData = false)
{
    var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString);

    if (logSql)
    {
        builder.LogTo(
            Console.WriteLine,
            new[] { DbLoggerCategory.Database.Command.Name },
            LogLevel.Information);
    }

    if (sensitiveData)
    {
        builder.EnableSensitiveDataLogging();
    }

    return new AppDbContext(builder.Options);
}

using (var setup = NewContext())
{
    setup.Database.Migrate();

    if (!setup.Quotes.Any())
    {
        Console.WriteLine($"Seeding {RowCount} quotes into {ConnectionString}...");

        for (var batchStart = 0; batchStart < RowCount; batchStart += 500)
        {
            for (var i = batchStart; i < batchStart + 500; i++)
            {
                var wordCount = (i % 20) + 1;
                var text = string.Join(' ', Enumerable.Range(0, wordCount).Select(w => $"word{w}"));
                var result = Quote.Create($"Author {i % 100}", text);
                setup.Quotes.Add(result.Value!);
            }

            setup.SaveChanges();
            setup.ChangeTracker.Clear();
        }

        Console.WriteLine("Seed complete.");
    }
    else
    {
        Console.WriteLine($"Quotes table already has {setup.Quotes.Count()} rows, skipping seed.");
    }
}

Part1_LogGeneratedSql();
Part2_WholeEntityVsProjection();
Part3_ClientEvaluation();

void Part1_LogGeneratedSql()
{
    Console.WriteLine();
    Console.WriteLine("=== Part 1: log the generated SQL ===");
    Console.WriteLine($"DB: {ConnectionString}");
    Console.WriteLine("LINQ: db.Quotes.Where(q => q.Author == \"Author 7\").OrderBy(q => q.Id).Take(3)");
    Console.WriteLine();

    using var ctx = NewContext(logSql: true, sensitiveData: true);

    var quotes = ctx.Quotes
        .Where(q => q.Author == "Author 7")
        .OrderBy(q => q.Id)
        .Take(3)
        .ToList();

    Console.WriteLine($"Rows returned: {quotes.Count}");
}

void Part2_WholeEntityVsProjection()
{
    Console.WriteLine();
    Console.WriteLine("=== Part 2: whole entity vs projection ===");

    Console.WriteLine();
    Console.WriteLine("-- Whole entity (QuoteRepository.GetQuotesAsync's shape) --");
    Console.WriteLine("LINQ: db.Quotes.OrderBy(q => q.Id).Skip(0).Take(5).ToListAsync()");
    using (var ctx = NewContext(logSql: true))
    {
        var quotes = ctx.Quotes.OrderBy(q => q.Id).Skip(0).Take(5).ToList();
        Console.WriteLine($"Rows returned: {quotes.Count}, first row Text length = {quotes[0].Text.Length} chars");
    }

    Console.WriteLine();
    Console.WriteLine("-- Projected to QuoteSummaryDto(Id, Author) --");
    Console.WriteLine("LINQ: db.Quotes.OrderBy(q => q.Id).Skip(0).Take(5).Select(q => new QuoteSummaryDto(q.Id, q.Author))");
    using (var ctx = NewContext(logSql: true))
    {
        var summaries = ctx.Quotes
            .OrderBy(q => q.Id)
            .Skip(0)
            .Take(5)
            .Select(q => new QuoteSummaryDto(q.Id, q.Author))
            .ToList();

        Console.WriteLine($"Rows returned: {summaries.Count}");
    }
}

void Part3_ClientEvaluation()
{
    Console.WriteLine();
    Console.WriteLine("=== Part 3: catching an accidental client-side evaluation ===");

    Console.WriteLine();
    Console.WriteLine("-- Attempt 1: word-count filter directly in Where() --");
    Console.WriteLine("LINQ: db.Quotes.Where(q => q.Text.Split(' ').Length <= 5)");
    using (var ctx = NewContext(logSql: true))
    {
        try
        {
            var shortQuotes = ctx.Quotes
                .Where(q => q.Text.Split(' ').Length <= 5)
                .OrderBy(q => q.Id)
                .Take(5)
                .ToList();

            Console.WriteLine($"Rows returned: {shortQuotes.Count}");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Caught: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("-- Attempt 2: \"fix\" by calling ToList() before the filter --");
    Console.WriteLine("LINQ: db.Quotes.ToList().Where(q => q.Text.Split(' ').Length <= 5).OrderBy(q => q.Id).Take(5)");
    using (var ctx = NewContext(logSql: true))
    {
        var all = ctx.Quotes.ToList();
        var shortQuotes = all
            .Where(q => q.Text.Split(' ').Length <= 5)
            .OrderBy(q => q.Id)
            .Take(5)
            .ToList();

        Console.WriteLine($"Rows fetched from the DB: {all.Count}, rows actually wanted: {shortQuotes.Count}");
    }

    Console.WriteLine();
    Console.WriteLine("-- Fix: translatable predicate narrows it server-side first, word count stays client-side on purpose --");
    Console.WriteLine("LINQ: db.Quotes.Where(q => q.Text.Length <= 50).ToList()  ...then .Where(word count) in memory");
    using (var ctx = NewContext(logSql: true))
    {
        var candidates = ctx.Quotes.Where(q => q.Text.Length <= 50).ToList();

        // Text.Split isn't translatable, so this part genuinely has to run in memory --
        // the point is it now runs over `candidates`, not the whole table.
        var shortQuotes = candidates
            .Where(q => q.Text.Split(' ').Length <= 5)
            .OrderBy(q => q.Id)
            .Take(5)
            .ToList();

        Console.WriteLine($"Rows fetched from the DB: {candidates.Count}, rows actually wanted: {shortQuotes.Count}");
    }
}

record QuoteSummaryDto(int Id, string Author);
