-- =====================================================================
-- Day 7 - Task 2: Window functions
-- =====================================================================
-- Target database: day - 2/QuotesApi/quotes.db (SQLite)
-- Schema: the REAL existing EF Core schema for QuotesApi. No tables
-- were added or altered, and no application code was touched to write
-- this script.
--
-- Real table used: Quotes(Id PK, Author, Text, IsDeleted)
--   - Id is INTEGER PRIMARY KEY AUTOINCREMENT (no CreatedAt/timestamp
--     column exists - same limitation already documented in
--     day - 7/task - 1). Where a window needs an ORDER BY that means
--     "time", Id is used as an insertion-order proxy, exactly as in
--     Task 1's most-recent-quote query.
--   - IsDeleted = 0 excludes the one soft-deleted row, reproducing the
--     application's EF Core global query filter for these raw queries.
--
-- Data snapshot at the time this was written (non-deleted rows,
-- ordered by Id): Id 1 'Authorized' (10 chars), Id 3 'Test' (10 chars),
-- Id 4 'Mahatma Gandhi' (42 chars), Id 5 'AI Agent' (27 chars),
-- Id 6 'Jaeger' (42 chars), Id 7 'Task6 Verification' (56 chars).
-- Note the genuine ties in character length (10 and 42) - these are
-- real, not fabricated, and are what make Section 2 (RANK) meaningful.
-- =====================================================================


-- =====================================================================
-- SECTION 1 - ROW_NUMBER()
-- =====================================================================
-- ROW_NUMBER() assigns a unique, strictly increasing integer to every
-- row in the window, in the order defined by ORDER BY - even when rows
-- tie on the primary ordering expression. Here quotes are numbered by
-- length (longest first), with Id as an explicit tiebreaker so that
-- the two 10-char quotes and the two 42-char quotes still each get a
-- distinct row number instead of sharing one. Contrast this with
-- Section 2, which uses the same LENGTH(Text) ordering but WITHOUT the
-- Id tiebreaker, so real ties show through instead of being broken.
WITH NonDeletedQuotes AS (
    SELECT Id, Author, Text, LENGTH(Text) AS TextLength
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Id,
    Author,
    Text,
    TextLength,
    ROW_NUMBER() OVER (ORDER BY TextLength DESC, Id ASC) AS RowNum
FROM NonDeletedQuotes
ORDER BY RowNum;


-- =====================================================================
-- SECTION 2 - RANK()
-- =====================================================================
-- RANK() also orders rows, but rows with equal values in the ORDER BY
-- expression receive the SAME rank, and the next distinct value's rank
-- then skips ahead by the number of tied rows (unlike DENSE_RANK, which
-- would not skip). The real data has two genuine ties in character
-- length - 42 chars (Ids 4 and 6) and 10 chars (Ids 1 and 3) - so this
-- is not a staged example: expect ranks 1, 2, 2, 4, 5, 5 (rank 3 and 6
-- are skipped because two rows each tie for rank 2 and rank 5).
WITH NonDeletedQuotes AS (
    SELECT Id, Author, Text, LENGTH(Text) AS TextLength
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Id,
    Author,
    Text,
    TextLength,
    RANK() OVER (ORDER BY TextLength DESC) AS LengthRank
FROM NonDeletedQuotes
ORDER BY LengthRank, Id;


-- =====================================================================
-- SECTION 3 - LAG()
-- =====================================================================
-- LAG() reads a value from a PRECEDING row in the window's ORDER BY
-- sequence, without a self-join or correlated subquery. Ordering is by
-- Id, used here strictly as an insertion-order proxy (there is no
-- timestamp column in this schema - see day - 7/task - 1/README.md for
-- why). For each quote, this shows the Author/Text of the
-- previously-inserted non-deleted quote. The first row in the sequence
-- (Id = 1) has no earlier row, so its LAG columns are NULL.
WITH NonDeletedQuotes AS (
    SELECT Id, Author, Text
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Id,
    Author,
    Text,
    LAG(Author) OVER (ORDER BY Id) AS PreviousAuthor,
    LAG(Text) OVER (ORDER BY Id)   AS PreviousText
FROM NonDeletedQuotes
ORDER BY Id;


-- =====================================================================
-- SECTION 4 - LEAD()
-- =====================================================================
-- LEAD() is the mirror of LAG(): it reads a value from a FOLLOWING row
-- in the same Id-ordered (insertion-order proxy) sequence. For each
-- quote, this shows the Author/Text of the next-inserted non-deleted
-- quote. The last row in the sequence (Id = 7) has no later row, so its
-- LEAD columns are NULL.
WITH NonDeletedQuotes AS (
    SELECT Id, Author, Text
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Id,
    Author,
    Text,
    LEAD(Author) OVER (ORDER BY Id) AS NextAuthor,
    LEAD(Text) OVER (ORDER BY Id)   AS NextText
FROM NonDeletedQuotes
ORDER BY Id;


-- =====================================================================
-- SECTION 5 - RUNNING TOTAL: SUM() OVER (ORDER BY ...)
-- =====================================================================
-- Adding ORDER BY to SUM() OVER (...) turns it from a single aggregate
-- into a running/cumulative total: for each row, it sums the current
-- row's value together with every preceding row's value in the window's
-- order, instead of the whole partition's total. Ordered by Id
-- (insertion-order proxy), this accumulates the total number of
-- characters written across all non-deleted quotes so far, i.e. "how
-- many characters had been recorded in the Quotes table by the time
-- this quote was inserted."
WITH NonDeletedQuotes AS (
    SELECT Id, Author, LENGTH(Text) AS TextLength
    FROM Quotes
    WHERE IsDeleted = 0
)
SELECT
    Id,
    Author,
    TextLength,
    SUM(TextLength) OVER (ORDER BY Id) AS RunningCharacterTotal
FROM NonDeletedQuotes
ORDER BY Id;
