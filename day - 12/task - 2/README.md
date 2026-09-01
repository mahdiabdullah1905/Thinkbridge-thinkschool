# Day 12 — Task 2: When to reach for Dapper

## Query reimplemented

`GetQuoteListQuery` from [day - 12/task - 1](../task - 1/README.md) — the
`GET /api/quotes` list read, projected to `QuoteListItem` (id, author,
truncated `TextPreview`, and a correlated `AuthorQuoteCount`). It was the
obvious pick per the task's own instructions: it's already the one read
in this codebase built as a MediatR query with a denormalized read model,
and it's the "hardest" LINQ in the project — a correlated scalar subquery
inside a `Select`, which is exactly the kind of query worth checking
against hand-written SQL instead of inventing a new one just to have
something to reimplement.

## What got added

- `QuotesApi/Queries/GetQuoteListDapperQuery.cs` +
  `GetQuoteListDapperQueryHandler.cs` — a second `IRequestHandler` for a
  new `GetQuoteListDapperQuery`, returning the exact same
  `PaginatedResponse<QuoteListItem>` shape as the EF version. No new
  domain model.
- `Dapper` 2.1.79 added to `QuotesApi.csproj` — the only new package.
  `Microsoft.Data.Sqlite` was already a transitive dependency of
  `Microsoft.EntityFrameworkCore.Sqlite`.
- `QuotesApi.Tests/EfVsDapperQuoteListEquivalenceTests.cs` — automated
  correctness check, not just a manual note.
- `day - 12/task - 2/Task2.csproj` + `Program.cs` — a standalone console
  benchmark, same shape as `day - 10/task - 2` and `day - 11/task - 1`'s
  seed projects: project-references `QuotesApi.csproj`, seeds its own
  scratch SQLite file, times both handlers.

**`MapQuoteEndpoints`'s `GET /api/quotes` is untouched** — it still
dispatches `GetQuoteListQuery` (the EF version) exactly as it did after
Task 1. `GetQuoteListDapperQuery` isn't wired to any endpoint; it's only
ever invoked from the test above and the benchmark console app. EF stays
the default for the reason the task states going in: this is about
learning when Dapper earns its place, not replacing EF.

## SQL

**EF** (captured from `GetQuoteListQueryHandler`'s LINQ — same SQL Task 1
verified with `LogTo`, reproduced in the benchmark's query-plan section
below):

```sql
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
LIMIT @p1 OFFSET @p
```

**Dapper** (`GetQuoteListDapperQueryHandler.PageSql`, written by hand):

```sql
SELECT
    q."Id" AS "Id",
    q."Author" AS "Author",
    CASE
        WHEN length(q."Text") <= @PreviewLength THEN q."Text"
        ELSE substr(q."Text", 1, @PreviewLength) || '...'
    END AS "TextPreview",
    (SELECT COUNT(*) FROM "Quotes" q0 WHERE q0."IsDeleted" = 0 AND q0."Author" = q."Author") AS "AuthorQuoteCount"
FROM "Quotes" q
WHERE q."IsDeleted" = 0
ORDER BY q."Id"
LIMIT @Size OFFSET @Offset
```

Not byte-for-byte identical, and it shouldn't be pretended otherwise:

- EF writes the boolean filter as `NOT ("q"."IsDeleted")` (from its
  `HasQueryFilter(q => !q.IsDeleted)`); the hand-written version spells
  the same predicate as `"IsDeleted" = 0`. Same soft-delete filter, two
  ways to write "false" against a SQLite `INTEGER`-backed bool column.
- EF's substring start is `0 + 1` — the literal `0` is C#'s
  `Substring(0, ...)` argument surviving translation into SQLite's
  1-based `substr`; hand-written SQL just writes `1`. Cosmetic, not a
  behavioral difference.
- EF parameterizes the page size/offset as anonymous `@p`/`@p1`; the
  Dapper version names them `@Size`/`@Offset` (and additionally
  parameterizes the 120-character cutoff as `@PreviewLength`, where EF
  inlines it as a literal because it's a C# constant baked into the
  expression tree, not a query parameter).

None of that changes what rows come back or how the database executes
the query — confirmed below, not assumed.

**`EXPLAIN QUERY PLAN` for both, same database, same LIMIT/OFFSET
(page=1, size=20):**

```
EF:
id=7  parent=0  notused=216  detail=SCAN q
id=23  parent=0  notused=0  detail=CORRELATED SCALAR SUBQUERY 1
id=29  parent=23  notused=62  detail=SEARCH q0 USING INDEX IX_Quotes_Author (Author=?)

Dapper:
id=7  parent=0  notused=216  detail=SCAN q
id=23  parent=0  notused=0  detail=CORRELATED SCALAR SUBQUERY 1
id=29  parent=23  notused=62  detail=SEARCH q0 USING INDEX IX_Quotes_Author (Author=?)
```

Identical plans: a full scan of `Quotes` for the page (there's no
selective `WHERE` beyond the soft-delete flag, so nothing for an index to
narrow), and for each of those rows, the correlated `AuthorQuoteCount`
subquery seeks `IX_Quotes_Author` (the index Day 11 Task 2 added) instead
of scanning. Same shape, same index usage, both ways of writing it.

## Correctness check

`EfVsDapperQuoteListEquivalenceTests.cs` seeds a small fixed dataset
(mixed authors, one quote deliberately 150 characters to force
truncation) into a shared file-backed SQLite database, runs both
handlers against it, and asserts field-by-field on every row — `Id`,
`Author`, `TextPreview`, `AuthorQuoteCount` — plus a second test for page
2 of a 5-row set, to check an offset that isn't just "the first page."
Both pass:

```
dotnet test day - 2/QuotesApi.Tests --filter EfVsDapperQuoteListEquivalenceTests

Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

The benchmark console app also runs its own correctness check against
the full 10,000-row seeded set before timing anything, and prints it
rather than assuming the unit tests generalize to that dataset:

```
=== Correctness check (page=1, size=20) ===
EF:     20 rows, totalCount=10000, first id=1, last id=20
Dapper: 20 rows, totalCount=10000, first id=1, last id=20
Result: MATCH - every field on every row is identical.
```

**One real bug this caught along the way**: the first version of
`GetQuoteListDapperQueryHandler` queried straight into `QuoteListItem`
and threw at runtime —
`A parameterless default constructor or one matching signature
(Int64 Id, String Author, String TextPreview, Int64 AuthorQuoteCount)
is required for QuotesApi.Models.QuoteListItem materialization`.
Microsoft.Data.Sqlite always reads a SQLite `INTEGER` column back as
`long`, because SQLite's storage class is a dynamic, up-to-8-byte
integer with no static width — EF's LINQ translation knows from the
expression tree that the target is `int` and narrows it during
materialization, but Dapper's constructor-matching materializer just
checks the reader's runtime type against `QuoteListItem`'s constructor
parameters and refuses to narrow silently. Fixed by reading into a
private `QuoteListRow(long Id, string Author, string TextPreview, long
AuthorQuoteCount)` and mapping to `QuoteListItem` with two explicit
`(int)` casts. Worth keeping in the diff instead of quietly avoiding it,
since it's exactly the kind of thing "EF's translator does more for you
than a raw reader does" means in practice.

## Timing

Benchmark: `day - 12/task - 2`, run against a freshly seeded 10,000-row
/ 500-author SQLite database (same shape as Day 11's seed, for
continuity), `page=1, size=20` — the same request on both sides, 5
warm-up calls discarded, then 50 measured calls each, `median`/`min`/`max`
over wall-clock milliseconds:

```text
EF Core:
median: 1.935 ms   min: 1.574 ms   max: 10.711 ms

Dapper:
median: 1.273 ms   min: 1.007 ms   max: 2.868 ms
```

Dapper's median was faster in every run of this benchmark, but the
absolute numbers moved noticeably between runs (medians from 1.9–4.8 ms
for EF and 1.3–3.0 ms for Dapper across four consecutive runs on this
machine while other processes were active) — reported here as one
representative run, not cherry-picked for the widest gap. The **ratio**
was consistent across all of them: Dapper's median came in at roughly
60–70% of EF's, run after run. `max` is noisy on both sides (EF's spiked
to 122 ms in one run, almost certainly a GC pause or a stalled disk read
on this dev machine, not a query-plan regression — the plan above didn't
change between runs) and isn't treated as a reliable number here, unlike
`median`.

## Allocations / Span\<T\>

Measured with `GC.GetAllocatedBytesForCurrentThread()` around each call
(before/after delta), same 50 iterations as the timing above:

```text
EF Core:    median allocated: 80,816 bytes
Dapper:     median allocated: 44,560 bytes
```

Dapper allocates roughly 55% of what the EF path does per call. This
tracks with what each side actually does: EF's path goes through change
tracking setup, LINQ expression compilation/caching, and its query
pipeline even for a no-tracking projection, on top of materializing the
same 20 rows; Dapper skips all of that and only builds the `SqliteCommand`,
runs the reader, and maps 20 rows.

**`Span<T>` doesn't show up in either query handler, on purpose.** Both
the EF LINQ and the raw SQL do the `TextPreview` truncation inside the
`CASE` expression — the database does the substring work, and the CLR
side never touches a partial string to slice. There's no client-side
`string.Substring` call on the hot path in either version for `Span<T>`
to make cheaper; inventing one just to use the tag's second half would
be optimizing code that doesn't exist in this task. The one place a
memory primitive actually pulls weight is in the benchmark harness
itself: `Median()` in `Program.cs` sorts the collected timing samples
in place via `CollectionsMarshal.AsSpan(samples)` instead of
`samples.OrderBy(...).ToArray()`, which would allocate a whole new
sequence just to find a middle value that gets read once. Small, but a
real (if minor) use of a memory primitive for the benchmark's own
bookkeeping rather than a forced one in the mapping code.

## The rule

At this table's size (10,000 rows) and this query's shape, Dapper is a
real but modest win — roughly 30–40% less median latency and about 45%
less allocation per call, not a 10x jump. That matches what's actually
different between the two paths here: same SQL, same index, same rows
out; the gap is EF's tracking/pipeline overhead on top of an otherwise
identical query, not a smarter query plan.

**Rule for a teammate**: default to EF Core. Reach for Dapper on a
specific read only after profiling (not guessing) has shown that read is
actually hot — the kind of thing Day 11 Task 2 did for `/api/authors`,
where the fix that mattered was eliminating an N+1 and adding an index,
not swapping the ORM — and only once the query's shape is stable enough
that hand-written SQL won't need to be rewritten every time the read
model changes. A query that's already a single well-understood
projection, like this one, is a reasonable Dapper candidate precisely
*because* it's simple enough that "hand-written SQL" is three lines
different from what EF already generates. A read that's still evolving,
or that benefits from EF's change tracking or complex include graphs, is
not — the maintenance cost of a second, hand-synced SQL string isn't
worth a 30-40% win on a path nobody has measured as a bottleneck yet.

## Files

| File | What it is |
|---|---|
| `day - 2/QuotesApi/Queries/GetQuoteListDapperQuery.cs`, `GetQuoteListDapperQueryHandler.cs` | The Dapper read path: same request/response shape as `GetQuoteListQuery`, hand-written SQL |
| `day - 2/QuotesApi/QuotesApi.csproj` | `Dapper` 2.1.79 package reference added |
| `day - 2/QuotesApi.Tests/EfVsDapperQuoteListEquivalenceTests.cs` | Automated EF-vs-Dapper correctness check |
| `day - 12/task - 2/Task2.csproj`, `Program.cs` | Standalone benchmark: seeds a scratch db, runs the correctness check, prints both query plans, times both handlers |

`GET /api/quotes` and everything else in `day - 2/QuotesApi` outside the
two new files above is unchanged.

## Reproducing

```powershell
# Automated correctness test
cd "day - 2/QuotesApi.Tests"
dotnet test --filter EfVsDapperQuoteListEquivalenceTests

# Full benchmark (seeds day12-task2-bench.db on first run, gitignored)
cd "../../day - 12/task - 2"
dotnet run -c Release
```

## Limitations

- One machine, one run reported as the representative sample (with the
  four-run range quoted above for honesty about variance) — not a
  statistically rigorous benchmark. Good enough to see which way the gap
  points and roughly how big it is, not precise enough to defend a
  specific percentage in a performance review.
- 10,000 rows is small enough that both paths finish in low single-digit
  milliseconds; neither is "slow" here. The point of this task was
  learning what changes between EF and Dapper on an equivalent query, not
  proving Dapper always wins or manufacturing a crisis to justify it.
- SQLite's `Microsoft.Data.Sqlite` provider doesn't do real asynchronous
  I/O under the hood (its `async` methods run synchronously and complete
  their `Task` immediately) — this doesn't invalidate the comparison
  (both handlers pay the same provider cost), but it does mean this
  benchmark says nothing about how the gap would behave under real
  concurrent I/O contention against, say, Postgres or SQL Server.
