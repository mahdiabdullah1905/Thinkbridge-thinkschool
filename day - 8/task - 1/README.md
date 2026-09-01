# Day 8 — Task 1: Clustered vs non-clustered indexes

## Setup

Ran against a standalone SQL Server 2022 container, started manually from
the same image (`mcr.microsoft.com/mssql/server:2022-latest`) that
`day - 3/task - 7`'s Testcontainers integration tests use — but this is a
separate, one-off container, not the Testcontainers-managed instance:

```
docker run -d --name day8-mssql-experiment \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<your-password>" \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Database `Day8IndexDemo`, table `dbo.Orders`, 100,000 rows generated
deterministically from the row number alone (no `RAND()`/`NEWID()` — see
Section 2 of `index-comparison.sql`), so re-running the script produces
the same data every time.

```
dbo.Orders
  OrderId    INT
  CustomerId INT
  OrderDate  DATE
  Status     VARCHAR(20)
  Amount     DECIMAL(10,2)
  Notes      CHAR(200)   -- padding so a table scan has a realistic page cost
```

## Indexes and the query each one backs

| Index | Query |
|---|---|
| `CIX_Orders_OrderId` (clustered) | `WHERE OrderId = 54321` |
| `IX_Orders_CustomerId` (non-clustered, `INCLUDE (OrderDate, Amount)`) | `WHERE CustomerId = 777` |
| `IX_Orders_OrderDate` (non-clustered, `INCLUDE (CustomerId, Status)`) | `WHERE OrderDate BETWEEN '2024-06-01' AND '2024-06-07'` |

## Logical reads, before / after (`SET STATISTICS IO ON`)

| Query | Rows returned | Before (heap, no indexes) | After (index added) |
|---|---|---|---|
| `OrderId = 54321` | 1 | 3046 | 3 |
| `CustomerId = 777` | 100 | 3046 | 3 |
| `OrderDate` range (7 days) | 959 | 3046 | 7 |

The three "before" numbers are identical because with no index at all,
every one of these queries falls back to the same full heap scan
regardless of the predicate.

## Confirming the indexes are actually used

`sqlcmd` has no graphical plan viewer, so instead of just trusting the
logical-read drop, I captured the real **actual** execution plan
(post-execution, via `SET STATISTICS XML ON` — Section 7 of the script)
and read the operator/index directly out of the returned XML:

- `OrderId` query → `PhysicalOp="Clustered Index Seek"`,
  `Object ... Index="[CIX_Orders_OrderId]" IndexKind="Clustered"`,
  `ActualRows="1"`, `ActualLogicalReads="3"`
- `CustomerId` query → `PhysicalOp="Index Seek"`,
  `Object ... Index="[IX_Orders_CustomerId]" IndexKind="NonClustered"`,
  `ActualRows="100"`, `ActualLogicalReads="3"`
- `OrderDate` query → `PhysicalOp="Index Seek"`,
  `Object ... Index="[IX_Orders_OrderDate]" IndexKind="NonClustered"`,
  `ActualRows="959"`, `ActualLogicalReads="7"`

Each plan names the exact index just created, shows it as a Seek (not a
Scan), and its `ActualLogicalReads` matches the `STATISTICS IO` numbers
above exactly — so this isn't inferred from the read counts alone.

**To see this graphically:** connect SSMS or Azure Data Studio to
`localhost,1433` (sa / the password used in the `docker run` above), turn
on "Include Actual Execution Plan," and re-run the three queries in
Section 7. Look for the rightmost operator on each plan — it should be a
Clustered Index Seek / Index Seek icon (not Table/Clustered Index Scan),
and its tooltip's "Object" line should name the index from the table
above.

## Write-side cost

Inserted the same 500 rows once against the bare heap and once with all
three indexes in place, rolling back both times so the 100k-row baseline
was never actually changed. `STATISTICS IO` on `Orders` went from **515**
logical reads (heap only) to **3139**, plus a new **1025**-logical-read
sort worktable that didn't exist before. That's the real cost of keeping
3 extra B-trees in key order on every insert instead of just appending to
a heap — the same indexes that cut these three queries' reads by roughly
1000x made this insert noticeably more expensive to maintain.

## Reproducing

`index-comparison.sql` has every DDL statement and query in the order
they were actually run, sectioned and commented. Run each section in
order against a fresh database (Section 8 has two sub-parts — 8a runs
before Section 4, 8b after Section 6 — see the comments in that section).
