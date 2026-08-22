using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Queries;

const string ConnectionString = "Data Source=day12-task2-bench.db";
const int AuthorCount = 500;
const int QuotesPerAuthor = 20; // same scale as day - 11's seed (10,000 rows), for continuity
const int BenchPage = 1;
const int BenchSize = 20;
const int WarmupIterations = 5;
const int MeasuredIterations = 50;

var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options;

using (var setup = new AppDbContext(options))
{
    setup.Database.Migrate();

    if (!setup.Quotes.Any())
    {
        Console.WriteLine($"Seeding {AuthorCount * QuotesPerAuthor} quotes across {AuthorCount} authors into {ConnectionString}...");

        for (var a = 0; a < AuthorCount; a++)
        {
            for (var i = 0; i < QuotesPerAuthor; i++)
            {
                // word count cycles 1..30 so some quotes land comfortably under the 120-char
                // preview cutoff and some land well past it - both branches of the CASE in
                // GetQuoteListQueryHandler/GetQuoteListDapperQueryHandler get exercised.
                var wordCount = ((a * QuotesPerAuthor + i) % 30) + 1;
                var text = string.Join(' ', Enumerable.Range(0, wordCount).Select(w => $"word{w}"));
                var result = Quote.Create($"Author {a}", text);
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

await CorrectnessCheck();
PrintQueryPlans();
await TimingComparison();

async Task CorrectnessCheck()
{
    Console.WriteLine();
    Console.WriteLine($"=== Correctness check (page={BenchPage}, size={BenchSize}) ===");

    using var context = new AppDbContext(options);
    var efResult = await new GetQuoteListQueryHandler(context).Handle(new GetQuoteListQuery(BenchPage, BenchSize), CancellationToken.None);
    var dapperResult = await new GetQuoteListDapperQueryHandler(context).Handle(new GetQuoteListDapperQuery(BenchPage, BenchSize), CancellationToken.None);

    var efItems = efResult.Items.ToList();
    var dapperItems = dapperResult.Items.ToList();

    var mismatches = new List<string>();
    if (efResult.TotalCount != dapperResult.TotalCount)
        mismatches.Add($"TotalCount: EF={efResult.TotalCount} Dapper={dapperResult.TotalCount}");
    if (efItems.Count != dapperItems.Count)
        mismatches.Add($"Item count: EF={efItems.Count} Dapper={dapperItems.Count}");

    for (var i = 0; i < Math.Min(efItems.Count, dapperItems.Count); i++)
    {
        var (ef, dp) = (efItems[i], dapperItems[i]);
        if (ef.Id != dp.Id || ef.Author != dp.Author || ef.TextPreview != dp.TextPreview || ef.AuthorQuoteCount != dp.AuthorQuoteCount)
        {
            mismatches.Add($"Row {i}: EF={ef} Dapper={dp}");
        }
    }

    Console.WriteLine($"EF:     {efItems.Count} rows, totalCount={efResult.TotalCount}, first id={efItems[0].Id}, last id={efItems[^1].Id}");
    Console.WriteLine($"Dapper: {dapperItems.Count} rows, totalCount={dapperResult.TotalCount}, first id={dapperItems[0].Id}, last id={dapperItems[^1].Id}");
    Console.WriteLine($"Sample row 0 - EF: {efItems[0]}");
    Console.WriteLine($"Sample row 0 - Dapper: {dapperItems[0]}");

    Console.WriteLine(mismatches.Count == 0
        ? "Result: MATCH - every field on every row is identical."
        : "Result: MISMATCH -\n  " + string.Join("\n  ", mismatches));
}

void PrintQueryPlans()
{
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString);
    connection.Open();

    Console.WriteLine();
    Console.WriteLine("=== EXPLAIN QUERY PLAN: EF's generated SQL (GetQuoteListQueryHandler's LINQ, captured in day - 12/task - 1's README) ===");
    RunPlan(connection, """
        SELECT "q"."Id", "q"."Author", CASE
            WHEN length("q"."Text") <= 120 THEN "q"."Text"
            ELSE substr("q"."Text", 0 + 1, 120) || '...'
        END, (
            SELECT COUNT(*)
            FROM "Quotes" AS "q0"
            WHERE NOT ("q0"."IsDeleted") AND "q0"."Author" = "q"."Author")
        FROM "Quotes" AS "q"
        WHERE NOT ("q"."IsDeleted")
        ORDER BY "q"."Id"
        LIMIT 20 OFFSET 0
        """);

    Console.WriteLine();
    Console.WriteLine("=== EXPLAIN QUERY PLAN: Dapper's literal page SQL (GetQuoteListDapperQueryHandler.PageSql) ===");
    RunPlan(connection, """
        SELECT
            q."Id" AS "Id",
            q."Author" AS "Author",
            CASE WHEN length(q."Text") <= 120 THEN q."Text" ELSE substr(q."Text", 1, 120) || '...' END AS "TextPreview",
            (SELECT COUNT(*) FROM "Quotes" q0 WHERE q0."IsDeleted" = 0 AND q0."Author" = q."Author") AS "AuthorQuoteCount"
        FROM "Quotes" q
        WHERE q."IsDeleted" = 0
        ORDER BY q."Id"
        LIMIT 20 OFFSET 0
        """);
}

void RunPlan(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "EXPLAIN QUERY PLAN\n" + sql;

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var parts = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            parts.Add($"{reader.GetName(i)}={reader.GetValue(i)}");
        }

        Console.WriteLine(string.Join("  ", parts));
    }
}

async Task TimingComparison()
{
    Console.WriteLine();
    Console.WriteLine($"=== Timing (warmup={WarmupIterations}, measured={MeasuredIterations}, page={BenchPage}, size={BenchSize}, {AuthorCount * QuotesPerAuthor} rows / {AuthorCount} authors) ===");

    for (var i = 0; i < WarmupIterations; i++)
    {
        await RunEf();
        await RunDapper();
    }

    var efMs = new List<double>();
    var efBytes = new List<double>();
    var dapperMs = new List<double>();
    var dapperBytes = new List<double>();

    for (var i = 0; i < MeasuredIterations; i++)
    {
        var (efElapsed, efAllocated) = await RunEf();
        efMs.Add(efElapsed);
        efBytes.Add(efAllocated);

        var (dapperElapsed, dapperAllocated) = await RunDapper();
        dapperMs.Add(dapperElapsed);
        dapperBytes.Add(dapperAllocated);
    }

    Console.WriteLine();
    Console.WriteLine("EF Core:");
    Console.WriteLine($"  median: {Median(efMs):F3} ms   min: {efMs.Min():F3} ms   max: {efMs.Max():F3} ms");
    Console.WriteLine($"  median allocated: {Median(efBytes):N0} bytes");

    Console.WriteLine();
    Console.WriteLine("Dapper:");
    Console.WriteLine($"  median: {Median(dapperMs):F3} ms   min: {dapperMs.Min():F3} ms   max: {dapperMs.Max():F3} ms");
    Console.WriteLine($"  median allocated: {Median(dapperBytes):N0} bytes");
}

async Task<(double ElapsedMs, long AllocatedBytes)> RunEf()
{
    using var context = new AppDbContext(options);
    var handler = new GetQuoteListQueryHandler(context);

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    var result = await handler.Handle(new GetQuoteListQuery(BenchPage, BenchSize), CancellationToken.None);
    sw.Stop();
    var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

    if (result.Items.Count() != BenchSize) throw new InvalidOperationException("EF handler returned an unexpected row count mid-benchmark.");
    return (sw.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore);
}

async Task<(double ElapsedMs, long AllocatedBytes)> RunDapper()
{
    using var context = new AppDbContext(options);
    var handler = new GetQuoteListDapperQueryHandler(context);

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    var result = await handler.Handle(new GetQuoteListDapperQuery(BenchPage, BenchSize), CancellationToken.None);
    sw.Stop();
    var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

    if (result.Items.Count() != BenchSize) throw new InvalidOperationException("Dapper handler returned an unexpected row count mid-benchmark.");
    return (sw.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore);
}

// The one place this task's "Span<T> + memory primitives" tag actually fits: sorting the
// collected samples in place via CollectionsMarshal.AsSpan instead of `samples.OrderBy(...)`,
// which would allocate a whole new sequence just to find a median. It doesn't belong in the
// query handlers - both of them do their truncation in SQL (see the CASE expression in both
// PageSql and the EF LINQ), so there's no client-side string slicing on the hot path for
// Span<T> to help with there.
double Median(List<double> samples)
{
    var span = CollectionsMarshal.AsSpan(samples);
    span.Sort();
    var mid = span.Length / 2;
    return span.Length % 2 == 0 ? (span[mid - 1] + span[mid]) / 2.0 : span[mid];
}
