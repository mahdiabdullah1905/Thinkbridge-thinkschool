# Day 7 — Task 1: Joins and CTEs at depth

## 1. What this task was teaching

Day 7 is about getting fluent with SQL joins (`INNER`, `LEFT`, `CROSS`) and
Common Table Expressions (CTEs), both non-recursive and recursive, against a
**real** schema rather than a toy one. The capstone exercise is a single SQL
statement that returns, per author: their quote count and their most recent
quote — built with a CTE instead of a correlated subquery in the `SELECT`
list.

Everything in this task is SQL-only. The QuotesApi application itself was
not modified, no EF Core migration was added, and no new tables were
created.

## 2. The actual schema used

This uses the real, existing SQLite database at `day - 2/QuotesApi/quotes.db`,
as created by QuotesApi's existing EF Core migrations. The relevant tables:

```
Quotes
  Id         INTEGER PRIMARY KEY AUTOINCREMENT
  Author     TEXT NOT NULL
  Text       TEXT NOT NULL
  IsDeleted  INTEGER NOT NULL   -- soft-delete flag

Collections
  Id       INTEGER PRIMARY KEY AUTOINCREMENT
  Name     TEXT NOT NULL
  OwnerId  TEXT NOT NULL

CollectionItem
  Id            INTEGER PRIMARY KEY AUTOINCREMENT
  QuoteId       INTEGER NOT NULL   -- references Quotes.Id (not an enforced FK)
  AddedAt       TEXT NOT NULL      -- DateTimeOffset
  CollectionId  INTEGER NOT NULL   -- FK -> Collections.Id (enforced)
```

`CollectionId` on `CollectionItem` is a real, EF-enforced foreign key to
`Collections.Id`. `QuoteId` is a plain integer column that is used as a
reference to `Quotes.Id` in application code, but it is not declared as an
EF Core navigation/foreign key.

At the time this was written, `Quotes` had real rows (including one
soft-deleted row), but `Collections` and `CollectionItem` had zero rows in
the shipped `quotes.db`. Sections 1 and 2 below are still written against
this real relationship — see section 10 of this README for how they were
verified.

## 3. Why no Authors table was introduced

The existing schema has no `Authors` table. `Author` is a plain string
column directly on `Quotes` — quotes are not linked to a normalized author
entity. Adding an `Authors` table would mean inventing a new table and
migration purely to make this exercise look cleaner, which was explicitly
out of scope for this task. Instead, `Quotes.Author` is treated as the
author identifier throughout, and "per-author" queries group directly on
that string column.

## 4. INNER JOIN (Section 1 of the .sql file)

`Collections INNER JOIN CollectionItem INNER JOIN Quotes` lists every
collection together with the quotes it actually contains. Because `INNER
JOIN` only keeps rows where both sides match, a collection with no items
yet does not appear in the result at all, and an item pointing at a
soft-deleted quote is filtered out by the `IsDeleted = 0` condition on the
final join.

## 5. LEFT JOIN vs. INNER JOIN (Section 2)

The same three tables, but joined with `LEFT JOIN` starting from
`Collections`. `LEFT JOIN` keeps every row from the left-hand table
(`Collections`) even when there is no matching row on the right — an
empty collection still appears, just with `NULL` in the `CollectionItem`/
`Quotes` columns. That is the concrete difference from Section 1: rows
INNER JOIN drops (empty collections) are the exact rows LEFT JOIN
preserves.

## 6. CROSS JOIN and why it's dangerous (Section 3)

`CROSS JOIN` has no `ON` condition — it returns the full Cartesian product,
every row on one side paired with every row on the other, so the row count
multiplies (`N × M`). On any table of meaningful size that gets huge very
fast, so it should only ever be used deliberately and bounded.

No throwaway table was created for this. The script cross-joins the real
`Quotes` table against itself (`Quotes q1 CROSS JOIN Quotes q2`) to
enumerate every possible pairing of two different, non-deleted quotes. The
`WHERE q1.Id < q2.Id` filter is not part of the join condition — it's the
deliberate bounding step that drops mirrored pairs and self-pairs, cutting
the raw Cartesian product roughly in half. With 6 non-deleted quotes in the
current data, this returns 15 pairs (`6 choose 2`) instead of the 36 rows a
completely unfiltered self cross-join would produce.

## 7. Non-recursive CTE (Section 4)

A non-recursive CTE is a named subquery, scoped to the statement that
follows it, that can be referenced like a table. `AuthorQuoteStats`
computes, in one place, each author's quote count, their first/last quote
`Id`, and their average quote length — all in a single `GROUP BY` pass over
`Quotes`. The final `SELECT` then just reads from that named result. This
is meaningfully more than `WITH x AS (SELECT * FROM Quotes) SELECT * FROM
x` — it does real aggregation work that the outer query reuses.

## 8. What the recursive CTE demonstrates (Section 5)

**The existing Week-1 schema has no hierarchical or self-referencing
relationship** — `Quotes` doesn't reference other `Quotes`, `Collections`
don't nest, nothing in this schema is a tree or graph. Nothing was added to
fake one. Section 5 is a demonstration of recursive CTE *mechanics* in
SQLite, not a domain requirement.

The recursive CTE builds a small sequence of `Id` values, starting at the
real `MIN(Id)` in `Quotes` and stepping up one at a time. The recursive
step's `WHERE` clause hard-caps it at 10 rows
(`MIN(Id) ... MIN(Id) + 9`) regardless of how big `Quotes` ever grows, so
it cannot run away. That generated sequence is then `LEFT JOIN`ed back to
the real `Quotes` table, so each row in the output says whether a
non-deleted quote actually exists at that `Id` — which, incidentally, also
makes visible the gap left by the one soft-deleted row (`Id = 2`) in the
real data, since the join condition filters on `IsDeleted = 0`.

## 9. The final query (Section 6): author, quote count, most recent quote

```sql
WITH AuthorStats AS (
    SELECT
        Author,
        COUNT(*) AS QuoteCount,
        MAX(Id)  AS MostRecentQuoteId
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT
    s.Author     AS Author,
    s.QuoteCount AS QuoteCount,
    q.Text       AS MostRecentQuote
FROM AuthorStats s
INNER JOIN Quotes q
    ON q.Id = s.MostRecentQuoteId
    AND q.IsDeleted = 0
ORDER BY s.Author;
```

`AuthorStats` groups the real `Quotes` table by `Author` and computes both
the count and the `Id` of the most recent (highest-`Id`) quote in one pass.
The outer `SELECT` then joins that result back to `Quotes` once, on the
primary key `Id`, to pull the actual quote text.

## 10. Why a CTE instead of a correlated subquery

A correlated subquery version would look like:

```sql
SELECT
    q.Author,
    (SELECT COUNT(*) FROM Quotes q2 WHERE q2.Author = q.Author) AS QuoteCount,
    (SELECT q3.Text FROM Quotes q3
     WHERE q3.Author = q.Author
     ORDER BY q3.Id DESC LIMIT 1) AS MostRecentQuote
FROM Quotes q
GROUP BY q.Author;
```

That re-runs both subqueries once *per row of the outer query*, so cost
grows with the number of quotes, not just the number of authors. The CTE
version computes the aggregation exactly once (`GROUP BY Author` over the
whole table), then joins back to `Quotes` a single time on the primary key.
It's both clearer to read and cheaper to execute, and — per this task's
requirement — the final `SELECT` contains no correlated subquery at all.

`Id` is the `Quotes` primary key, so `q.Id = s.MostRecentQuoteId` can match
at most one row. Combined with one row per author out of `GROUP BY
Author`, that's what guarantees exactly one output row per author, with no
duplicates.

## 11. Why `MAX(Id)` is used as the "most recent" proxy

See the limitation called out below — there is no timestamp column to sort
by. `Quotes.Id` is an `INTEGER PRIMARY KEY AUTOINCREMENT` in SQLite, which
means it is assigned in strictly increasing order as rows are inserted. A
higher `Id` was therefore always inserted after a lower one, which makes
`MAX(Id)` a valid — if indirect — way to find "the last quote inserted for
this author."

## 12. Limitation: no timestamp column

**The existing `Quotes` table has no `CreatedAt`/timestamp column, so
"most recent" cannot be determined from actual time data. `Quotes.Id` is
AUTOINCREMENT and is therefore used as an insertion-order proxy for this
exercise. This is a limitation of the existing Week-1 schema, not a claim
that `Id` is a true timestamp.** No `CreatedAt` column and no migration
were added to this repository as part of this task, per the explicit scope
decision for Day 7 Task 1.

## Soft deletes

`QuotesApi`'s `AppDbContext` applies a global EF Core query filter
(`HasQueryFilter(q => !q.IsDeleted)`) so the application never sees
soft-deleted quotes through EF. Because these are raw SQL queries running
directly against SQLite (not through EF Core), that filter does not apply
automatically — every query in this file that touches `Quotes` explicitly
adds `WHERE IsDeleted = 0` (or the equivalent join condition) to reproduce
the same exclusion.

## Verifying Sections 1–2 (INNER/LEFT JOIN) despite empty tables

At the time of writing, `Collections` and `CollectionItem` had zero rows in
the real `quotes.db`, so running Sections 1–2 against it correctly returns
0 rows for both — which doesn't visibly show the INNER-vs-LEFT distinction.
To confirm the join logic itself was correct, a **disposable copy** of
`quotes.db` was made in a scratch/temp location, seeded with a few sample
`Collections`/`CollectionItem` rows referencing real, existing `Quotes`
rows (including one collection deliberately left empty), the two queries
were run against that copy, and the copy was then deleted. That confirmed:
the empty collection was dropped by `INNER JOIN` and preserved (with
`NULL` quote columns) by `LEFT JOIN`, exactly as documented in the .sql
file. **The real `quotes.db` was never written to** — it was only ever
opened read-only for validation.
