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

void ShowMissingIndexPlan()
{
    using var ctx = NewContext();
    var connection = ctx.Database.GetDbConnection();
    connection.Open();

    using var cmd = connection.CreateCommand();
    cmd.CommandText =
        $"EXPLAIN QUERY PLAN SELECT \"Id\", \"Author\", \"Text\", \"IsDeleted\" " +
        $"FROM \"Quotes\" WHERE \"Author\" = '{SampleAuthor}' AND \"IsDeleted\" = 0;";

    Console.WriteLine();
    Console.WriteLine("=== EXPLAIN QUERY PLAN: the per-author query /api/authors runs once per author ===");
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
