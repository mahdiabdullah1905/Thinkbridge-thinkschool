# Day 10 — Task 1: EF Core change tracker + AsNoTracking

## Setup

Reuses `AppDbContext` and the `Quote` entity from `day - 2/QuotesApi` via a
project reference — no new DbContext, entity, or migration. `Task1.csproj`
just references `QuotesApi.csproj` and points a fresh `DbContextOptionsBuilder`
at its own SQLite file (`day10-tracking.db`, gitignored like every other
`*.db` in this repo), so this doesn't touch the dev database the other
QuotesApi tasks use.

`Program.cs` runs `Database.Migrate()` against that file (applies the
existing `QuotesApi.Migrations`) and seeds 10,000 `Quote` rows once
(`Author {i % 500}` / `Sample quote body number {i}.`, deterministic, no
randomness) if the table is empty. Everything below reuses that same table.

```
dotnet run -c Release
```

## 1. Change tracking

`Quote.Delete()` just flips a private-setter `IsDeleted` bool — enough to
watch the tracker without needing a second property. Ran inside a
transaction that gets rolled back, so nothing is left changed:

```
=== 1. Change tracking ===
Loaded quote 4 -> state = Unchanged
After quote.Delete()   -> state = Modified
After SaveChanges()    -> state = Unchanged
Row 4 IsDeleted, read back inside the same transaction: True
Row 4 IsDeleted in DB, after rollback:  False
```

- A plain tracked query (`ctx.Quotes.First(...)`) starts life as `Unchanged`
  in `ctx.Entry(quote).State` — the tracker already has a snapshot of it,
  nothing has to change for that.
- Calling `quote.Delete()` mutates a tracked property and the entry flips to
  `Modified` with no call to EF at all — this is pure change-tracker
  snapshot comparison, not something `SaveChanges` computes on the fly.
- `SaveChanges()` issues the `UPDATE` and then resets the entry back to
  `Unchanged` (that's EF, not me manually resetting it).
- Reading the row back *inside the same transaction* (a second,
  `AsNoTracking()` query on the same `ctx`, so it hits the DB rather than
  the identity map) confirms the `UPDATE` really happened: `True`.
- `tx.Rollback()`, then a brand-new `AppDbContext`/connection reads the row:
  `False` — the row is exactly as it was before this ran. First tried
  checking "before rollback" from a second, separate connection instead of
  the same transaction — that just showed `False` too, because a second
  SQLite connection can't see another connection's uncommitted write. That
  would have looked identical whether or not the rollback did anything, so
  it wasn't actually testing what it claimed to; reading inside the same
  transaction first is what makes this a real before/after.

## 2. Identity resolution

Uses the same pagination shape `QuoteRepository.GetQuotesAsync` already
queries with (`OrderBy(Id).Take(n)`), just without the repository
indirection, plus a point lookup by `Id` — both realistic, both already
present in this codebase's query patterns:

```
=== 2. Identity resolution ===
Tracked:      single query and page query return the same instance? True
AsNoTracking: single query and page query return the same instance? False
```

Tracked: `ctx.Quotes.Single(q => q.Id == 25)` and a separate
`ctx.Quotes.OrderBy(q => q.Id).Take(50).ToList()` both touch row 25. Two
independent LINQ queries, two round trips to SQLite — but the *second* one
never materializes a new `Quote` for id 25. The change tracker sees an
entity with that key already in its identity map from the first query and
hands back the existing instance, so `ReferenceEquals` is `true`.

`AsNoTracking()` on both queries turns off the identity map, so each query
materializes its own object for row 25 — `ReferenceEquals` is `false`, even
though it's the same context and the same row.

## 3. Tracked vs AsNoTracking, 10,000-row read

5 iterations each, fresh `AppDbContext` per iteration, forced GC + a
throwaway warm-up read before measuring so the first iteration isn't
paying for JIT/disk-cache warm-up. `GC.GetAllocatedBytesForCurrentThread()`
for allocations, `Stopwatch` for wall time, `ChangeTracker.Entries().Count()`
to show what's actually sitting in the tracker afterward.

```
=== 3. Reading all 10000 rows: tracked vs AsNoTracking ===
-- Tracked --
     88 ms      99,87,936 bytes allocated   ChangeTracker.Entries()=10000
    102 ms      98,55,048 bytes allocated   ChangeTracker.Entries()=10000
    608 ms      98,55,048 bytes allocated   ChangeTracker.Entries()=10000
    475 ms      98,55,048 bytes allocated   ChangeTracker.Entries()=10000
    476 ms      98,55,048 bytes allocated   ChangeTracker.Entries()=10000
  avg: 349.8 ms, 98,81,626 bytes
-- AsNoTracking --
    111 ms      39,72,040 bytes allocated   ChangeTracker.Entries()=0
    134 ms      39,71,968 bytes allocated   ChangeTracker.Entries()=0
    124 ms      39,71,968 bytes allocated   ChangeTracker.Entries()=0
     79 ms      39,71,968 bytes allocated   ChangeTracker.Entries()=0
     54 ms      39,71,968 bytes allocated   ChangeTracker.Entries()=0
  avg: 100.4 ms, 39,71,982 bytes
```

- `ChangeTracker.Entries()` is `10000` after the tracked read and `0` after
  the no-tracking one — the most direct evidence of what "tracked" actually
  means: every materialized row gets an entry (identity, original-values
  snapshot, state) that stays alive with the context.
- Allocations: **~9.9 MB tracked vs ~4.0 MB untracked, about 2.5x** — the
  gap that's actually stable across all 5 runs each side, unlike the timing.
  That's the entry objects and original-value snapshots the tracked path
  builds for every row so it can later diff them against `SaveChanges`.
- Timing is noisier — tracked ranged 88–608 ms in this run, no-tracking
  54–134 ms. The first two tracked iterations look suspiciously close to
  no-tracking; the process had likely not yet settled into workstation GC's
  steady state (allocating ~10 MB more per iteration eventually catches up
  in gen0 collection cost). Allocation count is deterministic per query
  shape and is the more trustworthy number here; wall-clock is included
  because the task asks for it, with this caveat attached rather than
  cherry-picked.

## Reproducing

```
cd "day - 10/task - 1"
dotnet run -c Release
```

First run seeds `day10-tracking.db` (10,000 rows, ~15s); later runs skip
straight to the three demonstrations. Delete the `.db`/`.db-shm`/`.db-wal`
files to reseed from scratch.
