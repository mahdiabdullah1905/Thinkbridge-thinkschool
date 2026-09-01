# Day 7 — Task 2: Window functions

## 1. What this task was teaching

Window functions compute a value across a set of rows *related to the
current row* (a "window") without collapsing those rows into one, the way
`GROUP BY` does. This task demonstrates four building blocks against the
real `Quotes` table: `ROW_NUMBER()`, `RANK()`, `LAG()`/`LEAD()`, and a
running total built from `SUM(...) OVER (ORDER BY ...)`.

Everything in this task is SQL-only. The QuotesApi application itself was
not modified, no EF Core migration was added, and no new tables were
created. The real `day - 2/QuotesApi/quotes.db` was used as-is.

## 2. Window functions vs. `GROUP BY`

`GROUP BY` collapses many rows into one row per group — you lose the
individual rows, keeping only aggregates. A window function (`... OVER
(...)`) keeps every original row and *attaches* a computed value to each
one (a rank, a neighboring row's value, a running total). That's why
Section 5 below can show both a per-row `TextLength` and a
`RunningCharacterTotal` side by side — `GROUP BY` alone could not do that
without a self-join or correlated subquery.

## 3. The real schema and data used

Same table as Day 7 Task 1: `Quotes(Id PK, Author, Text, IsDeleted)`. No
`CreatedAt`/timestamp column exists — that limitation, already documented
in `day - 7/task - 1/README.md`, applies here too. At the time this was
written, `Quotes` had 7 rows, one of them soft-deleted (`IsDeleted = 1`),
leaving 6 non-deleted rows across 6 distinct authors (every author
currently has exactly one quote).

Every query below starts from a small CTE that filters
`WHERE IsDeleted = 0`, reproducing the same exclusion the application's
EF Core global query filter (`HasQueryFilter(q => !q.IsDeleted)`) applies
automatically — these are raw SQL queries, so that filter has to be
written explicitly.

## 4. `ROW_NUMBER()` (Section 1)

`ROW_NUMBER()` gives every row in the window a unique, gapless, increasing
integer, in `ORDER BY` order — even rows that tie on the ordering
expression still get different numbers, because ties are broken by
whatever comes first in the row source once the primary expression is
equal. Here quotes are numbered by `LENGTH(Text) DESC`, with `Id ASC` as
an explicit tiebreaker, so the two 10-character quotes and the two
42-character quotes each still get distinct row numbers (5 and 6, 2 and 3)
instead of sharing one.

## 5. `RANK()` and how ties behave (Section 2)

`RANK()` uses the same ordering (`LENGTH(Text) DESC`) but *without* an
`Id` tiebreaker, so real ties in the data show through instead of being
broken. The current data has two genuine ties: two quotes at 42 characters
and two at 10 characters. Executing the query produces ranks
`1, 2, 2, 4, 5, 5` — the tied 42-character rows both get rank 2, and the
next rank jumps to 4 (rank 3 is skipped); the tied 10-character rows both
get rank 5, and rank 6 is skipped. This is exactly how `RANK()` is
supposed to behave on ties: **equal input → equal rank, and the next
distinct value's rank skips ahead by the number of tied rows.** This was
verified against the real data, not staged.

## 6. `LAG()` and `LEAD()` (Sections 3 and 4)

`LAG()` reads a value from the row *before* the current one in the
window's `ORDER BY` sequence; `LEAD()` reads a value from the row *after*
it — both without a self-join or correlated subquery. Both are ordered by
`Id`, used strictly as an **insertion-order proxy**: there is no timestamp
column in this schema, so `Id` (an `INTEGER PRIMARY KEY AUTOINCREMENT`) is
the only column that reflects the order quotes were inserted in, exactly
as already established for the "most recent quote" query in Task 1. The
first row in the sequence has no predecessor, so its `LAG` columns come
back `NULL`; the last row has no successor, so its `LEAD` columns come
back `NULL`.

## 7. Running total: `SUM() OVER (ORDER BY ...)` (Section 5)

Adding `ORDER BY` inside `SUM(...) OVER (...)` changes it from a single
aggregate (one total for the whole set) into a **running/cumulative
total**: for each row, it sums that row's value together with every
preceding row's value in the window's order, rather than summing
everything at once. Ordered by `Id` (again, the insertion-order proxy),
this accumulates the total number of characters recorded in `Quotes` as
each non-deleted quote was inserted — i.e., after the 4th quote by
insertion order, 187 characters had been written across all quotes in
total (10 + 10 + 42 + 27 + 42 + 56).

## 8. Why `Id` was chosen for ordering (and why `LENGTH(Text)` elsewhere)

`Id` is used for `LAG`/`LEAD` and the running total because those two
constructs are inherently about *sequence* (what came before/after, what
accumulated over time), and `Id` is the only column in this schema that
reflects insertion order. `LENGTH(Text)` is used for `ROW_NUMBER`/`RANK`
instead, because those two are about *ranking by a value*, and
`LENGTH(Text)` is a real, derived-but-not-fabricated metric that happens
to contain genuine ties in the current data — which is what makes the
`RANK()` tie behavior demonstrable at all.

## 9. Limitations carried over from the existing Week-1 schema

- **No timestamp column.** As in Task 1, "insertion order" is inferred
  from the `Id` primary key, not from real date/time data. This is a
  schema limitation, not a claim that `Id` is a timestamp.
- **One quote per author (currently).** Every author in the real data has
  exactly one non-deleted quote, so partitioning any of these window
  functions `PARTITION BY Author` would trivially assign rank/row-number 1
  to every single row and demonstrate nothing about ties or ordering
  within a partition. For that reason, none of the queries partition by
  author — they run over the whole non-deleted `Quotes` set instead, which
  is where the real ties and sequencing actually exist to observe.

## 10. What was and wasn't done

The real `day - 2/QuotesApi/quotes.db` was used, read-only, to execute and
verify every query in `window-functions.sql`. No rows were inserted,
updated, or deleted in that database, no schema/migration changes were
made, and the QuotesApi application code was not touched.
