using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

const string ConnectionString = "Data Source=day11-loadtest.db";
const int AuthorCount = 500;
const int QuotesPerAuthor = 20;
const int RowCount = AuthorCount * QuotesPerAuthor;
const string SampleAuthor = "Author 250";

AppDbContext NewContext() =>
    new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(ConnectionString)
        .Options);

using (var setup = NewContext())
{
    setup.Database.Migrate();

    // WAL lets concurrent HTTP requests read the file without serializing behind SQLite's
    // default rollback-journal locking, so the load test measures the N+1/missing-index cost
    // itself instead of SQLite single-writer contention.
    setup.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    if (!setup.Quotes.Any())
    {
        Console.WriteLine($"Seeding {RowCount} quotes across {AuthorCount} authors into {ConnectionString}...");

        for (var a = 0; a < AuthorCount; a++)
        {
            for (var i = 0; i < QuotesPerAuthor; i++)
            {
                var result = Quote.Create($"Author {a}", $"Quote {i} from author {a}.");
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

ShowMissingIndexPlan();
ShowFixedQueryPlans();

void ShowMissingIndexPlan()
{
    using var ctx = NewContext();
    var connection = ctx.Database.GetDbConnection();
    connection.Open();

    using var cmd = connection.CreateCommand();
    cmd.CommandText =
        $"EXPLAIN QUERY PLAN SELECT \"Id\", \"Author\", \"Text\", \"IsDeleted\" " +
        $"FROM \"Quotes\" WHERE \"Author\" = '{SampleAuthor}' AND \"IsDeleted\" = 0;";

    // Task 1 originally captured this against a database with no IX_Quotes_Author, where it
    // printed "SCAN Quotes" (see task - 1/README.md section 2). The AddQuotesAuthorIndex
    // migration (added in Task 2) now runs as part of Database.Migrate() above, so this same
    // query shape on this same file prints the fixed plan from here on.
    Console.WriteLine();
    Console.WriteLine("=== EXPLAIN QUERY PLAN: the old per-author lookup query, WHERE Author = ? ===");
    Console.WriteLine(cmd.CommandText);
    Console.WriteLine();

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

void ShowFixedQueryPlans()
{
    using var ctx = NewContext();
    var connection = ctx.Database.GetDbConnection();
    connection.Open();

    // Task 2 fix: the actual SQL EF Core generates for the fixed /api/authors endpoint
    // (Quotes.GroupBy(q => q.Author).Select(g => new AuthorSummary(...))), captured verbatim
    // from the Development EF Core command log.
    Console.WriteLine();
    Console.WriteLine("=== AFTER N+1 fix: the single grouped query the fixed /api/authors issues ===");
    PrintPlan(connection, """
        SELECT "q1"."Author", "q1"."c", "q2"."Text", "q2"."Id"
        FROM (
            SELECT "q"."Author", COUNT(*) AS "c"
            FROM "Quotes" AS "q"
            WHERE NOT ("q"."IsDeleted")
            GROUP BY "q"."Author"
        ) AS "q1"
        LEFT JOIN (
            SELECT "q0"."Text", "q0"."Id", "q0"."Author"
            FROM "Quotes" AS "q0"
            WHERE NOT ("q0"."IsDeleted")
        ) AS "q2" ON "q1"."Author" = "q2"."Author"
        ORDER BY "q1"."Author";
        """);
}

void PrintPlan(System.Data.Common.DbConnection connection, string sql)
{
    Console.WriteLine(sql);
    Console.WriteLine();

    using var cmd = connection.CreateCommand();
    cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;

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
