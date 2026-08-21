# Day 11 — Task 1: Profile a slow endpoint

## Setup

Reuses `AppDbContext` and the `Quote` entity from `day - 2/QuotesApi` directly
— no new entity and no new migration. The slow endpoint itself lives in the
real app (`day - 2/QuotesApi/Extensions/ProgramExtensions.cs:131`,
`day - 2/QuotesApi/models/AuthorSummary.cs`, wired up in
`day - 2/QuotesApi/Program.cs`), since this task needs an actual running
HTTP server to profile under load, unlike Day 9/10's standalone console
demos.

**Why no `Authors` table.** Same decision as `day - 7/task - 1`: the real
schema has no `Authors` table, `Author` is a plain string column on
`Quotes`. Inventing one just to make this exercise look like a textbook
foreign key would mean a new table and migration purely for demo purposes,
which is out of scope here. The N+1 below is built directly against
`Quotes.Author`.

**Load-test data.** The real dev `quotes.db` only has a handful of rows
(see `day - 7/task - 1`'s README), too small to show anything under load.
`Seed/` is a standalone console project — same pattern as
`day - 10/task - 1`/`task - 2` — that project-references `QuotesApi.csproj`
and seeds its own, separate, gitignored SQLite file
(`Seed/day11-loadtest.db`) with 500 authors × 20 quotes each (10,000 rows,
deterministic: `Author {a}` / `Quote {i} from author {a}.`). The shared
`day - 2/QuotesApi/quotes.db` used by every other day's task is never
touched.

```
cd "day - 11/task - 1/Seed"
dotnet run -c Release
```

The seed script also sets `PRAGMA journal_mode=WAL` on that file. WAL lets
concurrent readers proceed without serializing behind SQLite's default
rollback-journal writer lock, so the load test below measures the N+1 /
missing-index cost itself rather than SQLite single-writer contention.

**Load test tool.** Neither `bombardier` nor `k6` was preinstalled here.
`winget install k6` hung with no progress for 15+ minutes, so `k6 v2.2.0`
was fetched directly from its official GitHub release
(`grafana/k6` — the project's own release artifact, not a third-party
mirror) and run from an unpacked folder instead of PATH.

## 1. The anti-pattern

`GET /api/authors` — an author directory: every author together with their
quotes. Written the quick way described in the task brief: one query for
the distinct author names, then one more query per author instead of a
single grouped query.

```csharp
group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
{
    var authors = await db.Quotes
        .Select(q => q.Author)
        .Distinct()
        .ToListAsync(ct);

    var summaries = new List<AuthorSummary>();

    foreach (var author in authors)
    {
        var quotesForAuthor = await db.Quotes
            .Where(q => q.Author == author)
            .ToListAsync(ct);

        summaries.Add(new AuthorSummary(author, quotesForAuthor.Count, quotesForAuthor.Select(q => q.Text).ToList()));
    }

    return Results.Ok(summaries);
});
```

That's 1 query to list the authors, then N more (one per author) instead of
a single `GROUP BY`/join — classic N+1. `Quotes.Author` has no index (see
`AppDbContextModelSnapshot.cs` — only `Users.Email` and the two
`RefreshTokens` columns are indexed), so each of those N per-author queries
falls back to a full table scan instead of a targeted lookup. The two
anti-patterns compound: it isn't just N extra round trips, it's N extra
full scans.

## 2. Missing index — execution plan evidence

`Seed/Program.cs` also runs `EXPLAIN QUERY PLAN` for the exact query shape
`/api/authors` issues once per author, against the seeded 10,000-row table:

```
EXPLAIN QUERY PLAN SELECT "Id", "Author", "Text", "IsDeleted" FROM "Quotes" WHERE "Author" = 'Author 250' AND "IsDeleted" = 0;

id=2  parent=0  notused=216  detail=SCAN Quotes
```

`SCAN Quotes` — not `SEARCH Quotes USING INDEX` — is SQLite's plan for a
full table scan. There's no index for the planner to seek on, so every one
of the 500 per-author queries the endpoint issues walks the whole table
looking for matching rows.

## 3. SQL emitted — N+1 evidence

Ran the app in `Development` (EF Core command logging is already set to
`Debug` there in `appsettings.Development.json`) pointed at the seeded db,
and issued **one isolated** `GET /api/authors` request (no concurrent
load). Trimmed excerpt in `sql-log-excerpt.txt`, full shape below:

```
--- Query 1 of 501: distinct author names ---
SELECT DISTINCT "q"."Author"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")

--- Query 2 of 501: first per-author query (Author 0) ---
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Author" = @author

... (499 more, identical shape, one per remaining author) ...
```

All 501 of these commands were logged under the exact same `TraceId`
(`0e55b4410a206d24fce0a5a182d43c71`) — i.e. one HTTP request, 501 database
round trips. Counted directly from the log
(`grep -c 'Executed DbCommand.*\[Parameters=\[@author'` plus the one
distinct-authors query), not estimated.

## 4. Single-request timing

Same isolated request, timed with `curl`, no concurrent load:

```
GET /api/authors        HTTP 200, 1.024s
GET /api/quotes?page=1&size=10   HTTP 200, 0.055s   (existing, already-fast endpoint)
```

~19x slower than the existing paginated quotes endpoint even with nothing
else hitting the server.

## 5. Load test — p50/p99 under concurrency

`load-test.js` runs two `k6` scenarios concurrently, 10 VUs each for 30s:
`authors_slow` hits the new endpoint, `quotes_baseline` hits the existing
`GET /api/quotes?page=1&size=10` at the same rate as a control. Full output
in `k6-results.txt`; summary:

| Endpoint | avg | p50 | p90 | p95 | p99 | max |
|---|---|---|---|---|---|---|
| `GET /api/authors` (N+1, no index) | 3.12s | 3.07s | 3.54s | 4.91s | 4.95s | 4.95s |
| `GET /api/quotes` (baseline) | 14.4ms | 12.9ms | 20.2ms | 25.2ms | 38.8ms | 363ms |

p50 is about **240x** slower than the baseline; p99 is about **127x**
slower. The gap is wider than the single-request number in section 4
because under 10 concurrent VUs, each request's 500 sequential per-author
round trips now also queue behind the 10 other VUs' round trips on the same
SQLite connection pool — the N+1 shape doesn't parallelize within a single
request, so concurrency multiplies its cost instead of just adding to it.

```
cd "day - 11/task - 1"
k6 run load-test.js                       # against http://localhost:5299 by default
k6 run -e BASE_URL=http://host:port load-test.js
```

## 6. Tying it together

- **N+1** (section 3): one request to `/api/authors` makes 501 database
  round trips instead of 1-2.
- **Missing index** (section 2): each of those 500 per-author queries is a
  full table scan (`SCAN Quotes`), not an index seek, because
  `Quotes.Author` has no index.
- **Combined effect** (sections 4-5): 500 full scans instead of 1 targeted
  query, repeated per request, is what turns a sub-100ms endpoint into a
  multi-second one, and the cost gets worse — not just additive — under
  concurrent load because SQLite serializes the many small round trips per
  VU against the same file.

A real fix would replace the loop with one grouped query
(`db.Quotes.GroupBy(q => q.Author)...`, or a single `WHERE Author IN (...)`
before grouping in memory) and add an index on `Quotes.Author` — but
implementing that fix is out of scope for this task, which only asks for
the slow endpoint and the profiling evidence.

## Limitation

- The Week-1 API's only configured provider is SQLite (`ProgramExtensions.cs`
  → `UseSqlite`), not a client-server RDBMS. WAL mode was turned on
  specifically to stop concurrent reads serializing behind SQLite's writer
  lock, but SQLite's per-connection overhead still isn't identical to a
  networked RDBMS. The root cause demonstrated here — N+1 query count ×
  missing-index full scan — is a data-access-layer problem that would
  reproduce (worse, since each round trip would cross the network) against
  SQL Server or Postgres too; the absolute latency numbers are specific to
  this SQLite setup.
- `k6` wasn't preinstalled on this machine and `winget install k6` hung
  with no visible progress; installed the official `v2.2.0` Windows release
  directly from `github.com/grafana/k6` instead.
- Run-to-run variance: three full k6 runs during this task showed
  `authors_slow` avg latency between 2.1s and 3.1s depending on what else
  had recently hit the process. The table above is from one clean, final
  run — server freshly restarted, no prior requests — captured verbatim in
  `k6-results.txt`.

## Files

| File | What it is |
|---|---|
| `Seed/Seed.csproj`, `Seed/Program.cs` | Seeds `day11-loadtest.db` (500 authors × 20 quotes) and prints the `EXPLAIN QUERY PLAN` in section 2 |
| `load-test.js` | k6 script: `authors_slow` vs `quotes_baseline` scenarios, 10 VUs / 30s each |
| `k6-results.txt` | Full console output of the final k6 run |
| `sql-log-excerpt.txt` | Trimmed EF Core SQL log for one isolated `/api/authors` request |
| `sample-response.json` | Trimmed shape of the endpoint's JSON response (real response is 500 authors, ~298 KB) |

## Reproducing end to end

```powershell
# 1. Seed the load-test database (from repo root)
cd "day - 11\task - 1\Seed"
dotnet run -c Release

# 2. Point the real QuotesApi at that file and run it (separate terminal)
$dbPath = (Resolve-Path "day11-loadtest.db").Path
cd "..\..\..\day - 2\QuotesApi"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=$dbPath"
dotnet run -c Release --no-launch-profile --urls http://localhost:5299

# 3. Load test it (third terminal, from day - 11/task - 1)
k6 run load-test.js
```
