# Day 10 — Task 2: Query translation + projections

## Setup

Reuses `AppDbContext` and the `Quote` entity from `day - 2/QuotesApi` the
same way `day - 10/task - 1` does — `Task2.csproj` project-references
`QuotesApi.csproj`, no new entity or migration. Its own SQLite file
(`day10-task2.db`, gitignored) gets seeded once with 2,000 quotes whose
word count cycles 1–20 (`Author {i % 100}` / `"word0 word1 ... word{k-1}"`),
so there's a real, varied `Text` column to filter and project on.

```
dotnet run -c Release
```

`NewContext(logSql, sensitiveData)` builds a plain `DbContextOptionsBuilder`
and only calls `LogTo(...)` / `EnableSensitiveDataLogging()` on the specific
instances passed `true` here, in this console project. `day - 2/QuotesApi`'s
own `Program.cs`/`appsettings.*.json` aren't touched, so nothing about the
real app's logging changes.

## Part 1 — Log the generated SQL

```
LINQ: db.Quotes.Where(q => q.Author == "Author 7").OrderBy(q => q.Id).Take(3)

Executed DbCommand (3ms) [Parameters=[@p='3'], CommandType='Text', CommandTimeout='30']
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" = 'Author 7'
ORDER BY "q"."Id"
LIMIT @p
Rows returned: 3
```

Captured via `LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information)`
against `day10-task2.db` — this is the actual SQL SQLite ran, not a guess at
what EF "should" produce. Two things worth calling out:

- `Author 7` is baked in as a literal, not a parameter — it's a string
  constant written directly in the lambda, and EF's SQL translator inlines
  those rather than parameterizing them (a captured variable would have
  come through as `@__author_0` instead).
- `Take(3)` *is* a parameter (`@p`), and its value shows up as `'3'`
  because this context has `EnableSensitiveDataLogging()` on. Part 2's
  contexts don't set that flag, and the difference shows immediately —
  see the `@p1='?'`/`@p='?'` below instead of real numbers.

## Part 2 — Whole entity vs projection

`QuoteRepository.GetQuotesAsync` (`day - 2/QuotesApi/Repositories/QuoteRepository.cs:24`)
is exactly this shape already: `_context.Quotes.Skip(...).Take(...).ToListAsync()`,
no `Select`. Ran the equivalent query both ways:

```
-- Whole entity (QuoteRepository.GetQuotesAsync's shape) --
LINQ: db.Quotes.OrderBy(q => q.Id).Skip(0).Take(5).ToListAsync()

SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
ORDER BY "q"."Id"
LIMIT @p1 OFFSET @p
Rows returned: 5, first row Text length = 5 chars

-- Projected to QuoteSummaryDto(Id, Author) --
LINQ: db.Quotes.OrderBy(q => q.Id).Skip(0).Take(5).Select(q => new QuoteSummaryDto(q.Id, q.Author))

SELECT "q"."Id", "q"."Author"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
ORDER BY "q"."Id"
LIMIT @p1 OFFSET @p
Rows returned: 5
```

Same predicate, same paging — the only difference is the `Select`, and the
column list in the logged SQL goes from all four mapped columns
(`Id, Author, IsDeleted, Text`) down to exactly the two the DTO asks for
(`Id, Author`). `Text` can be arbitrarily large; a page listing that only
needs id + author has no reason to pull it over the wire, materialize it,
and then throw it away. `QuoteSummaryDto` is declared locally in this
project (`record QuoteSummaryDto(int Id, string Author)`) rather than added
to `QuotesApi.Models`, since nothing there actually needs it yet.

## Part 3 — Catching an accidental client-side evaluation

Wanted "the first 5 short quotes" (word count ≤ 5). `Text.Split(' ').Length`
isn't something EF can turn into SQL:

```
-- Attempt 1: word-count filter directly in Where() --
LINQ: db.Quotes.Where(q => q.Text.Split(' ').Length <= 5)

Caught: The LINQ expression 'DbSet<Quote>()
    .Where(q => !(q.IsDeleted))
    .Where(q => ArrayLength(q.Text.Split( , None))
     <= 5)' could not be translated. Additional information: Translation of
method 'string.Split' failed. ... Either rewrite the query in a form that
can be translated, or switch to client evaluation explicitly by inserting a
call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
```

EF Core throws instead of silently doing something expensive — that part's
not the bug. The bug is the obvious-looking fix: take the error message at
its word and add a `ToList()`.

```
-- Attempt 2: "fix" by calling ToList() before the filter --
LINQ: db.Quotes.ToList().Where(q => q.Text.Split(' ').Length <= 5).OrderBy(q => q.Id).Take(5)

SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
Rows fetched from the DB: 2000, rows actually wanted: 5
```

The exception is gone and the result is correct, which is exactly why this
is the dangerous version — it ships. The logged SQL has no `WHERE` clause
at all beyond the soft-delete filter: `Where`, `OrderBy`, and `Take` all
moved to LINQ-to-Objects the moment `ToList()` ran, so every one of the
2,000 rows gets fetched and fully materialized just to keep 5 of them.
Nothing in the C# reads as wrong at a glance — it still says `.Where(...)`
and `.Take(5)` — the SQL log is what actually catches it.

```
-- Fix: translatable predicate narrows it server-side first, word count stays client-side on purpose --
LINQ: db.Quotes.Where(q => q.Text.Length <= 50).ToList()  ...then .Where(word count) in memory

SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND length("q"."Text") <= 50
Rows fetched from the DB: 800, rows actually wanted: 5
```

`Text.Length` *is* translatable (`length(...)` in the generated SQL), so a
generous, cheap-to-compute upper bound on word count runs in SQLite first —
800 rows instead of 2,000. `Text.Split(' ').Length` still runs client-side
after that, same as attempt 2, but now over 800 candidates instead of the
whole table, and it's a deliberate `.ToList()` with a comment explaining
why, not an accidental one reached for to make an exception disappear.

## Reproducing

```
cd "day - 10/task - 2"
dotnet run -c Release
```

First run seeds `day10-task2.db` (2,000 rows); later runs skip straight to
the three parts. Delete the `.db`/`.db-shm`/`.db-wal` files to reseed.
