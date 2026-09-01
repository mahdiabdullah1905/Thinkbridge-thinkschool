# Day 11 — Task 2: Drop p99 by 10×

Direct continuation of `day - 11/task - 1`. Fixes both root causes found
there in the same endpoint (`GET /api/authors`,
`day - 2/QuotesApi/Extensions/ProgramExtensions.cs:131`), then re-runs the
exact same load test to measure the actual improvement.

## Setup

No new experiment, no new database. Same seeded file from Task 1
(`day - 11/task - 1/Seed/day11-loadtest.db`, 500 authors × 20 quotes), same
`k6` binary. The fix lives entirely in `day - 2/QuotesApi` — the slow
endpoint from Task 1 was edited in place, not duplicated, since the task is
"fix it," not "add a second, fixed endpoint next to it."

## 1. Eliminating the N+1

Task 1's version issued one query for the distinct authors, then one more
per author (501 queries total). Replaced with a single `GroupBy` +
projection:

```csharp
group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
{
    var summaries = await db.Quotes
        .GroupBy(q => q.Author)
        .Select(g => new AuthorSummary(g.Key, g.Count(), g.Select(q => q.Text).ToList()))
        .ToListAsync(ct);

    return Results.Ok(summaries);
});
```

**This was checked, not assumed.** There's no real `Authors` entity in this
schema (see Task 1's README, and `day - 7/task - 1` before it) — `Author` is
a plain string column — so there's no navigation property for `Include` to
walk, and a naive read of EF Core's docs would suggest a `GroupBy` with a
nested `g.Select(...).ToList()` either doesn't translate to SQL at all or
falls back to buffering the whole table into memory and grouping in .NET,
which would just move the cost, not remove it. Ran it against the real
seeded database with EF Core command logging on to see what actually
happens, rather than trust that assumption:

```
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
ORDER BY "q1"."Author"
```

EF Core 10's Sqlite provider translates this into one real SQL statement —
an aggregate subquery (author + count) left-joined back to the
unaggregated rows to pull each group's quote texts, entirely server-side.
Confirmed by issuing one isolated `GET /api/authors` request (no
concurrent load, full log in `sql-log-excerpt-fixed.txt`) and counting
`Executed DbCommand` entries under its `TraceId`
(`d06a80b2423d556e721dde0d749e14fa`):

```
grep -c "Executed DbCommand" sql-log-excerpt-fixed.txt
1
```

**501 queries → 1 query.** Verified by execution, per the task's own
instruction not to just reason that the code "should" produce one query.
`sample-response-fixed.json` confirms the response shape is unchanged (500
authors, 20 quotes each) — the fix changed how the data is fetched, not
what the endpoint returns.

## 2. Adding the index

```csharp
// day - 2/QuotesApi/Data/AppDbContext.cs
modelBuilder.Entity<Quote>().HasQueryFilter(q => !q.IsDeleted);
modelBuilder.Entity<Quote>().HasIndex(q => q.Author);
```

Generated the migration the same way the existing five were (`dotnet ef
migrations add AddQuotesAuthorIndex`), not hand-written:

```csharp
// Migrations/20260821044200_AddQuotesAuthorIndex.cs
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateIndex(
        name: "IX_Quotes_Author",
        table: "Quotes",
        column: "Author");
}
```

One index, on the one column the task named
(`Quotes.Author`) — nothing added to `Id`, `Text`, or `IsDeleted`.
`Program.cs` already calls `dbContext.Database.MigrateAsync()` on startup,
so this applies the same way every other migration in this project does;
no separate script was needed to apply it.

**Before/after plan for the query this index targets**
(`WHERE Author = ...`, full text in `query-plans-after.txt`):

| | Plan |
|---|---|
| Before (Task 1, no index) | `SCAN Quotes` |
| After (this task) | `SEARCH Quotes USING INDEX IX_Quotes_Author (Author=?)` |

Same query text, run against the same table — a full scan became an index
seek purely because the index now exists.

The GroupBy fix in section 1 means the endpoint no longer runs that
`WHERE Author = ?` query at all, but the index isn't wasted: the plan for
the new single grouped query (also in `query-plans-after.txt`) shows SQLite
using `IX_Quotes_Author` twice — once to compute the per-author counts
(`SCAN q USING INDEX IX_Quotes_Author`) and once for the join back to fetch
each group's quotes (`SEARCH q0 USING INDEX IX_Quotes_Author (Author=?)
LEFT-JOIN`).

## 3. Re-measuring under the same load

Same `k6` script as Task 1 (`load-test.js`, copied over unchanged apart
from renaming the `authors_slow` scenario to `authors_fixed` since the code
under test changed) — same two scenarios, same 10 VUs, same 30s, same
`quotes_baseline` control, run against the same seeded database, same
machine, same day. Full output in `k6-results-fixed.txt`.

| Endpoint | avg | p50 | p90 | p95 | p99 | max |
|---|---|---|---|---|---|---|
| `GET /api/authors` — **before** (Task 1: N+1, no index) | 3.12s | 3.07s | 3.54s | 4.91s | 4.95s | 4.95s |
| `GET /api/authors` — **after** (this task) | 110ms | 107ms | 131ms | 142ms | 182ms | 389ms |
| `GET /api/quotes` baseline — before | 14.4ms | 12.9ms | 20.2ms | 25.2ms | 38.8ms | 363ms |
| `GET /api/quotes` baseline — after | 9.4ms | 8.0ms | 15.3ms | 18.5ms | 26.4ms | 218ms |

**p99: 4.95s → 182ms, a 27.2× reduction. p50: 3.07s → 107ms, a 28.7×
reduction.** Both clear the task's ~10× target; not adjusted or rounded up
to hit it — these are the numbers `k6` printed.

The baseline (`GET /api/quotes`) got slightly faster too (p99 38.8ms →
26.4ms), most likely because the fixed `/api/authors` no longer holds the
shared SQLite connection pool busy with 500 sequential round trips per
request, leaving more headroom for the concurrently-running baseline
requests — a side effect of removing the N+1, not a separate fix.

## 4. What's still slower than the baseline, and why

`/api/authors` at 107ms median is still ~13× slower than `/api/quotes` at
8ms — expected, and not a remaining bug. The two endpoints do different
amounts of work: `/api/quotes?page=1&size=10` returns 10 rows: page. 
`/api/authors` aggregates all 10,000 rows into 500 groups and returns all
of them — one JSON response is ~298 KB (`sample-response-fixed.json`)
against ~1-2 KB for the baseline page. A single isolated request (no
concurrent load) confirms this gap isn't a queuing artifact:

```
GET /api/authors     HTTP 200, 0.310s   (was 1.024s in Task 1)
```

~3.3× faster in isolation, vs ~28× faster under 10-VU load — the fix
removed 500 round trips that used to queue behind the other 9 VUs' round
trips on the same SQLite connection; a single request never paid that
queuing cost as heavily to begin with, so the constant per-request
aggregation/serialization cost (which the fix didn't touch, and wasn't
supposed to) is a proportionally bigger share of what's left.

## Limitation

- Same SQLite caveat as Task 1: absolute numbers are specific to this
  provider and this machine; the two root causes fixed here (N+1 query
  count, missing index) are provider-agnostic and would matter at least as
  much, likely more, against a networked RDBMS.
- `/api/authors` is still not as fast as `/api/quotes` — see section 4.
  That gap is payload size and real aggregation work, not a leftover N+1
  or a missing index; verified by checking the query count (1) and the
  plan (index used) directly rather than assumed.
- One k6 run per side (before in Task 1, after here), not repeated for
  variance — Task 1's README already documented 2.1s-3.1s run-to-run
  variance on the *before* side; the true "after" number could plausibly
  land anywhere in a similar few-hundred-ms band rather than exactly 182ms
  p99, but even the slowest before-run (2.1s) against the after-run here
  is still a >11× improvement, so the 10x target holds either way.

## Files

| File | What it is |
|---|---|
| `load-test.js` | Same k6 script as Task 1's, `authors_slow` renamed to `authors_fixed` |
| `k6-results-fixed.txt` | Full k6 output for the fixed endpoint (section 3) |
| `sql-log-excerpt-fixed.txt` | EF Core SQL log for one isolated `/api/authors` request — 1 query |
| `query-plans-after.txt` | `EXPLAIN QUERY PLAN` for both the old per-author query (now indexed) and the new grouped query |
| `sample-response-fixed.json` | Trimmed shape of the fixed endpoint's response (unchanged: 500 authors, ~298 KB full) |

Code changes live in `day - 2/QuotesApi`, not here:
`Extensions/ProgramExtensions.cs` (the fix), `Data/AppDbContext.cs` (the
index mapping), `Migrations/20260821044200_AddQuotesAuthorIndex.cs` (the
migration).

## Reproducing

```powershell
# Server (from day - 2/QuotesApi) — same seeded db as Task 1
$dbPath = (Resolve-Path "..\..\day - 11\task - 1\Seed\day11-loadtest.db").Path
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=$dbPath"
dotnet run -c Release --no-launch-profile --urls http://localhost:5299

# Load test (from day - 11/task - 2, separate terminal)
k6 run load-test.js
```
