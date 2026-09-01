# Day 8 — Task 2: Covering indexes and INCLUDEd columns

Reuses the table, data, and container from
[day - 8/task - 1](../task%20-%201/) — same `Day8IndexDemo` database,
same `dbo.Orders` (100,000 rows), same `day8-mssql-experiment` container.
Nothing from Task 1 was changed; this task only adds one new index.

## The query and why it Key-Lookups

```sql
SELECT OrderId, CustomerId, OrderDate, Amount, Notes
FROM dbo.Orders
WHERE CustomerId = 777;
```

Task 1's `IX_Orders_CustomerId (CustomerId) INCLUDE (OrderDate, Amount)`
covers the key column, the two included columns, and `OrderId` (the
clustering key, which a non-clustered index always carries as its row
locator) — but not `Notes`. Asking for `Notes` forces SQL Server to seek
the non-clustered index for the matching rows, then do a Key Lookup back
into the clustered index to fetch `Notes` for each one.

## Before: Key Lookup confirmed from the actual plan

Captured with `SET STATISTICS XML ON` (the real, post-execution plan —
not estimated):

- `Index Seek` on `IX_Orders_CustomerId` — `ActualRows="100"`,
  `ActualLogicalReads="3"`
- `Nested Loops`, feeding into →
- `Clustered Index Seek` on `CIX_Orders_OrderId`, with **`Lookup="1"`**
  set on that operator in the XML — this is exactly the attribute
  SSMS/Azure Data Studio reads to render it as a "Key Lookup" icon, not
  a plain seek. `ActualLogicalReads="270"`

`STATISTICS IO`: `Table 'Orders'. ... logical reads 318`.

## The covering index

```sql
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Covering
    ON dbo.Orders (CustomerId)
    INCLUDE (OrderDate, Amount, Notes);
```

Same key column, but `Notes` is now included too, so every column the
query asks for can come straight out of the index.

## After: Key Lookup gone, confirmed from the actual plan

Same query, re-run after the index above exists:

- A single `RelOp`: `PhysicalOp="Index Seek"` on
  `IX_Orders_CustomerId_Covering` — `ActualRows="100"`,
  `ActualLogicalReads="7"`
- No `Nested Loops`, no `Clustered Index Seek`, no `Lookup="1"` anywhere
  in the plan XML.

`STATISTICS IO`: `Table 'Orders'. ... logical reads 7` — down from 318.

**To see this graphically:** connect SSMS or Azure Data Studio to
`localhost,1433` (same container as Task 1), turn on "Include Actual
Execution Plan," and run Section 1 then Section 2 of
`covering-index.sql`. The first plan shows a Key Lookup operator joined
via Nested Loops; the second is a single Index Seek with no lookup.

## Reproducing

`covering-index.sql` has both sections in the order they were run.
Requires `day - 8/task - 1/index-comparison.sql` Sections 1, 2, 4 and 5
to have been run first against the same database, so `dbo.Orders` and
`IX_Orders_CustomerId` already exist.
