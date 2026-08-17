-- =====================================================================
-- Day 7 - Task 1: Joins and CTEs at depth
-- =====================================================================
-- Target database: day - 2/QuotesApi/quotes.db (SQLite)
-- Schema: the REAL existing EF Core schema for QuotesApi, exactly as
-- created by the existing migrations. No tables were added or altered
-- to write this script.
--
-- Real tables used below:
--   Quotes(Id PK, Author, Text, IsDeleted)
--   Collections(Id PK, Name, OwnerId)
--   CollectionItem(Id PK, QuoteId, AddedAt, CollectionId FK -> Collections.Id)
--
-- Notes on the data at the time this script was written:
--   - Quotes has real rows (including one soft-deleted row, IsDeleted = 1).
--   - Collections / CollectionItem exist as tables but had no rows yet
--     in the shipped quotes.db. Sections 1 and 2 are written to be
--     correct against this real relationship regardless of row count;
--     see the README for how they were sanity-checked against a
--     disposable, seeded COPY of the database (the real quotes.db file
--     was never modified).
-- =====================================================================


-- =====================================================================
-- SECTION 1 - INNER JOIN
-- =====================================================================
-- Real relationship: Collections 1---N CollectionItem, and CollectionItem
-- holds a QuoteId pointing at Quotes.
--
-- Goal: list every collection together with the quotes it actually
-- contains. INNER JOIN only keeps rows where a match exists on BOTH
-- sides, so a Collection with zero CollectionItem rows will NOT appear
-- here at all, and a CollectionItem whose quote was soft-deleted is
-- excluded by the IsDeleted filter on the final join.
SELECT
    c.Name    AS CollectionName,
    c.OwnerId AS CollectionOwner,
    q.Author  AS QuoteAuthor,
    q.Text    AS QuoteText,
    ci.AddedAt AS AddedAt
FROM Collections c
INNER JOIN CollectionItem ci
    ON ci.CollectionId = c.Id
INNER JOIN Quotes q
    ON q.Id = ci.QuoteId
    AND q.IsDeleted = 0
ORDER BY c.Name, ci.AddedAt;


-- =====================================================================
-- SECTION 2 - LEFT JOIN
-- =====================================================================
-- Same three tables as Section 1, but starting from Collections with
-- LEFT JOIN instead of INNER JOIN.
--
-- What LEFT JOIN preserves: every row from Collections (the "left"
-- table) is kept in the result even when it has no matching
-- CollectionItem row (a brand-new / empty collection) or when its
-- item's quote is missing/soft-deleted. For those rows, ci.* and q.*
-- come back as NULL instead of the row being dropped.
--
-- This is exactly the distinction with Section 1: an empty collection
-- disappears under INNER JOIN but shows up here with NULL quote
-- columns, which is the meaningful difference the two joins produce
-- on this data.
SELECT
    c.Name    AS CollectionName,
    c.OwnerId AS CollectionOwner,
    q.Author  AS QuoteAuthor,
    q.Text    AS QuoteText,
    ci.AddedAt AS AddedAt
FROM Collections c
LEFT JOIN CollectionItem ci
    ON ci.CollectionId = c.Id
LEFT JOIN Quotes q
    ON q.Id = ci.QuoteId
    AND q.IsDeleted = 0
ORDER BY c.Name, ci.AddedAt;


-- =====================================================================
-- SECTION 3 - CROSS JOIN
-- =====================================================================
-- CROSS JOIN produces the full Cartesian product: every row on the left
-- paired with every row on the right, with no ON condition to filter
-- the combinations. That means row count multiplies (N x M), which is
-- exactly why it must be used deliberately and kept bounded - on large
-- tables an unfiltered CROSS JOIN can blow up into millions of rows.
--
-- No fake table is introduced here: this cross-joins the real Quotes
-- table against itself (a self cross-join) to enumerate every possible
-- pairing of two DIFFERENT, non-deleted quotes - e.g. for a "quote of
-- the day vs. quote of the day" comparison feature. The WHERE clause
-- (q1.Id < q2.Id) is not an ON condition; it is there deliberately to
-- cut the raw Cartesian product roughly in half by dropping mirrored
-- pairs (A,B)/(B,A) and self-pairs (A,A), which is the kind of bounding
-- a CROSS JOIN needs in practice.
SELECT
    q1.Id     AS QuoteAId,
    q1.Author AS QuoteAAuthor,
    q2.Id     AS QuoteBId,
    q2.Author AS QuoteBAuthor
FROM Quotes q1
CROSS JOIN Quotes q2
WHERE q1.IsDeleted = 0
  AND q2.IsDeleted = 0
  AND q1.Id < q2.Id
ORDER BY q1.Id, q2.Id;


-- =====================================================================
-- SECTION 4 - NON-RECURSIVE CTE
-- =====================================================================
-- A non-recursive CTE is just a named, reusable subquery scoped to the
-- statement that follows it. Here it computes real per-author
-- statistics from Quotes in one place, then the final SELECT reads
-- from that named result instead of repeating the aggregation logic.
WITH AuthorQuoteStats AS (
    SELECT
        Author,
        COUNT(*)            AS QuoteCount,
        MIN(Id)             AS FirstQuoteId,
        MAX(Id)             AS LastQuoteId,
        AVG(LENGTH(Text))   AS AvgQuoteLength
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT
    Author,
    QuoteCount,
    FirstQuoteId,
    LastQuoteId,
    AvgQuoteLength
FROM AuthorQuoteStats
ORDER BY QuoteCount DESC, Author;


-- =====================================================================
-- SECTION 5 - RECURSIVE CTE
-- =====================================================================
-- IMPORTANT: the existing Week-1 schema has NO hierarchical or
-- self-referencing relationship (Quotes does not reference other
-- Quotes, Collections does not nest, etc.). Nothing was added to the
-- schema to fake one. This section exists purely to demonstrate the
-- MECHANICS of a recursive CTE in SQLite, not to model a real domain
-- hierarchy.
--
-- The recursive CTE below generates a small, hard-bounded sequence of
-- Ids starting at the real MIN(Id) in Quotes (so it works whatever the
-- actual data looks like), then LEFT JOINs that sequence back to the
-- real Quotes table to show, for each Id in the sequence, whether a
-- non-deleted quote actually exists at that Id. The WHERE clause in the
-- recursive step is a hard cap of 10 rows (MIN(Id) .. MIN(Id) + 9), so
-- this cannot recurse indefinitely regardless of table size.
WITH RECURSIVE IdSequence(Id) AS (
    SELECT MIN(Id) FROM Quotes
    UNION ALL
    SELECT Id + 1
    FROM IdSequence
    WHERE Id < (SELECT MIN(Id) FROM Quotes) + 9
)
SELECT
    s.Id     AS SequentialId,
    q.Author AS Author,
    q.Text   AS Text,
    CASE
        WHEN q.Id IS NULL THEN 'no non-deleted quote at this Id'
        ELSE 'quote exists'
    END AS Note
FROM IdSequence s
LEFT JOIN Quotes q
    ON q.Id = s.Id
    AND q.IsDeleted = 0
ORDER BY s.Id;


-- =====================================================================
-- SECTION 6 - FINAL REQUIRED QUERY
-- =====================================================================
-- One statement, one CTE, no correlated subquery in the SELECT list.
--
-- AuthorStats aggregates each author's quote count and the Id of their
-- most recent quote in a single GROUP BY pass over Quotes. "Most
-- recent" is approximated with MAX(Id): see the README for why - in
-- short, Quotes has no CreatedAt/timestamp column, and Id is an
-- AUTOINCREMENT primary key, so a higher Id was always inserted later.
--
-- The final SELECT then joins AuthorStats back to Quotes ONCE (on the
-- primary key Id) to pull the actual text of that most-recent quote.
-- Because MAX(Id) is computed only over rows where IsDeleted = 0, the
-- Id it returns is guaranteed to already be a non-deleted quote, so the
-- IsDeleted = 0 predicate does not need to be repeated on the join -
-- it is still made explicit below for readability and defense-in-depth
-- to protect against someone in future rewriting AuthorStats.
--
-- Id is the Quotes primary key, so at most one row can ever match
-- q.Id = s.MostRecentQuoteId, which is what guarantees exactly one
-- output row per author (no duplicates).
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
    s.Author               AS Author,
    s.QuoteCount           AS QuoteCount,
    q.Text                 AS MostRecentQuote
FROM AuthorStats s
INNER JOIN Quotes q
    ON q.Id = s.MostRecentQuoteId
    AND q.IsDeleted = 0
ORDER BY s.Author;
