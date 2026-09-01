using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

const string ConnectionString = "Data Source=day10-tracking.db";
const int RowCount = 10_000;
const int Iterations = 5;

AppDbContext NewContext() =>
    new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(ConnectionString)
        .Options);

using (var setup = NewContext())
{
    setup.Database.Migrate();

    if (!setup.Quotes.Any())
    {
        Console.WriteLine($"Seeding {RowCount} quotes into {ConnectionString}...");

        for (var batchStart = 0; batchStart < RowCount; batchStart += 1000)
        {
            for (var i = batchStart; i < batchStart + 1000; i++)
            {
                var result = Quote.Create($"Author {i % 500}", $"Sample quote body number {i}.");
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

Section1_ChangeTracking();
Section2_IdentityResolution();
Section3_TrackedVsNoTrackingRead();

void Section1_ChangeTracking()
{
    Console.WriteLine();
    Console.WriteLine("=== 1. Change tracking ===");

    using var ctx = NewContext();
    using var tx = ctx.Database.BeginTransaction();

    var quote = ctx.Quotes.First(q => q.Author == "Author 3");
    Console.WriteLine($"Loaded quote {quote.Id} -> state = {ctx.Entry(quote).State}");

    quote.Delete();
    Console.WriteLine($"After quote.Delete()   -> state = {ctx.Entry(quote).State}");

    ctx.SaveChanges();
    Console.WriteLine($"After SaveChanges()    -> state = {ctx.Entry(quote).State}");

    var storedInTx = ctx.Quotes.IgnoreQueryFilters().AsNoTracking().First(q => q.Id == quote.Id);
    Console.WriteLine($"Row {quote.Id} IsDeleted, read back inside the same transaction: {storedInTx.IsDeleted}");

    tx.Rollback();

    using (var verify = NewContext())
    {
        var stored = verify.Quotes.IgnoreQueryFilters().First(q => q.Id == quote.Id);
        Console.WriteLine($"Row {quote.Id} IsDeleted in DB, after rollback:  {stored.IsDeleted}");
    }
}

void Section2_IdentityResolution()
{
    Console.WriteLine();
    Console.WriteLine("=== 2. Identity resolution ===");

    const int TargetId = 25;

    using (var ctx = NewContext())
    {
        var single = ctx.Quotes.Single(q => q.Id == TargetId);
        var page = ctx.Quotes.OrderBy(q => q.Id).Take(50).ToList();
        var fromPage = page.First(q => q.Id == TargetId);

        Console.WriteLine($"Tracked:      single query and page query return the same instance? {ReferenceEquals(single, fromPage)}");
    }

    using (var ctx = NewContext())
    {
        var single = ctx.Quotes.AsNoTracking().Single(q => q.Id == TargetId);
        var page = ctx.Quotes.AsNoTracking().OrderBy(q => q.Id).Take(50).ToList();
        var fromPage = page.First(q => q.Id == TargetId);

        Console.WriteLine($"AsNoTracking: single query and page query return the same instance? {ReferenceEquals(single, fromPage)}");
    }
}

void Section3_TrackedVsNoTrackingRead()
{
    Console.WriteLine();
    Console.WriteLine($"=== 3. Reading all {RowCount} rows: tracked vs AsNoTracking ===");

    using (var warmup = NewContext())
    {
        warmup.Quotes.AsNoTracking().ToList();
    }

    var trackedRuns = new List<(long Ms, long Bytes, int TrackedEntries)>();
    var noTrackingRuns = new List<(long Ms, long Bytes, int TrackedEntries)>();

    for (var i = 0; i < Iterations; i++)
    {
        trackedRuns.Add(MeasureRead(tracked: true));
    }

    for (var i = 0; i < Iterations; i++)
    {
        noTrackingRuns.Add(MeasureRead(tracked: false));
    }

    PrintRuns("Tracked", trackedRuns);
    PrintRuns("AsNoTracking", noTrackingRuns);
}

(long Ms, long Bytes, int TrackedEntries) MeasureRead(bool tracked)
{
    using var ctx = NewContext();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();

    var rows = tracked ? ctx.Quotes.ToList() : ctx.Quotes.AsNoTracking().ToList();

    sw.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    if (rows.Count != RowCount)
    {
        throw new InvalidOperationException($"Expected {RowCount} rows, got {rows.Count}");
    }

    return (sw.ElapsedMilliseconds, allocated, ctx.ChangeTracker.Entries().Count());
}

void PrintRuns(string label, List<(long Ms, long Bytes, int TrackedEntries)> runs)
{
    Console.WriteLine($"-- {label} --");
    foreach (var run in runs)
    {
        Console.WriteLine($"  {run.Ms,5} ms   {run.Bytes,12:N0} bytes allocated   ChangeTracker.Entries()={run.TrackedEntries}");
    }

    Console.WriteLine($"  avg: {runs.Average(r => r.Ms):F1} ms, {runs.Average(r => r.Bytes):N0} bytes");
}
