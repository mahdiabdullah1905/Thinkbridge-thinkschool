# Day 9 — Task 1: Isolation levels and the read anomalies

## Setup

Reuses the standalone SQL Server 2022 container `day8-mssql-experiment`
and the `Day8IndexDemo.dbo.Orders` table from `day - 8/task - 1` — no new
database, schema, or table. `Orders` already has a known, deterministic
row (`OrderId = 54321`, `Amount = 4321.50`) and a `CustomerId` range
(1–1000) that leaves `CustomerId = 9999` guaranteed empty, which is
exactly what's needed to demonstrate a changed row and an appearing row.

`Day8IndexDemo` has `READ_COMMITTED_SNAPSHOT` off (confirmed via
`sys.databases`), so `READ COMMITTED` here is the lock-based flavor —
a concurrent reader blocks on a writer's exclusive lock rather than
reading a row-versioned snapshot. That's what makes the blocking
visible in `dirty-read.sql` Part B: the anomaly doesn't just stop, the
second session's `SELECT` sits there until the first session ends.

## Files

| File | Anomaly | Reproduced at | Prevented at |
|---|---|---|---|
| `dirty-read.sql` | Dirty read | `READ UNCOMMITTED` | `READ COMMITTED` |
| `non-repeatable-read.sql` | Non-repeatable read | `READ COMMITTED` | `REPEATABLE READ` |
| `phantom-read.sql` | Phantom read | `REPEATABLE READ` | `SERIALIZABLE` |

Each file is self-contained: Part A reproduces the anomaly at the
isolation level that permits it, Part B reproduces the same interaction
at the level that prevents it. Steps are numbered and labeled
`SESSION 1` / `SESSION 2` — open two query windows against
`Day8IndexDemo` and run each step in order, switching windows exactly
where the comments say to. Each step is its own batch (`GO`-separated),
so don't run a window top-to-bottom.

## What each anomaly actually is (and isn't)

- **Dirty read** — reading a value another transaction wrote but hasn't
  committed yet. Confirmed dirty because the writer then rolls back —
  the value the reader saw never existed as committed data.
- **Non-repeatable read** — the *same row*, read twice in one
  transaction, comes back different because another transaction
  updated and committed it in between.
- **Phantom read** — the *same predicate*, run twice in one
  transaction, returns a different row *count* because another
  transaction inserted (or deleted) a matching row and committed in
  between. No existing row changes value — a row appears/disappears.

`REPEATABLE READ` prevents the second kind (it locks rows it has read)
but not the third (it doesn't lock the predicate's key range) — that
distinction is exactly why `phantom-read.sql`'s Part A anomaly uses
`REPEATABLE READ`, not `READ COMMITTED`.

## Isolation-level comparison

| Anomaly | Lowest isolation level that prevents it |
|---|---|
| Dirty read | `READ COMMITTED` |
| Non-repeatable read | `REPEATABLE READ` |
| Phantom read | `SERIALIZABLE` |

## Validation performed

Every step in all three files was actually executed against
`day8-mssql-experiment` — two concurrent `sqlcmd` sessions per
scenario, ordered with an in-transaction `WAITFOR DELAY` on one side so
the other side's statement lands mid-transaction. Captured results:

- **Dirty read**: reader under `READ UNCOMMITTED` returned `99999.99`
  while the writer's update was still uncommitted; the writer then
  rolled back and a follow-up read confirmed `4321.50`. Re-run under
  `READ COMMITTED`, the same reader blocked until the writer rolled
  back, then returned `4321.50` — never `99999.99`.
- **Non-repeatable read**: under `READ COMMITTED`, one transaction read
  `4321.50` then `5000.00` for the same row, with the writer's `UPDATE`
  committing in between and never blocking. Under `REPEATABLE READ`,
  the writer's `UPDATE` blocked for ~15s until the reader committed,
  and both reads in that transaction returned `4321.50`.
- **Phantom read**: under `REPEATABLE READ`, a `COUNT(*)` went from `0`
  to `1` after a concurrent, unblocked `INSERT` for `CustomerId = 9999`
  committed mid-transaction. Under `SERIALIZABLE`, the same `INSERT`
  blocked for ~15s until the reader committed, and both counts in that
  transaction stayed `0`.

`Day8IndexDemo.dbo.Orders` was left at its original 100,000-row
baseline afterward — the dirty-read demo never commits, and the other
two demos' cleanup steps restore the row/delete the inserted rows.

## Limitation

Timing between the two sessions was coordinated with `WAITFOR DELAY`
for scripted validation, since the two `sqlcmd` processes couldn't be
driven interactively in this environment. When run by hand in two SSMS
or Azure Data Studio windows, the pauses in the comments (waiting to
switch windows) serve the same purpose — the actual locking behavior
is unaffected by how the pause happens.
