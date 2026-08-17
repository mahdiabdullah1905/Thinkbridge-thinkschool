-- =====================================================================
-- ILLUSTRATIVE SQL ONLY
-- These tables do not exist in the current QuotesApi/quotes.db.
-- These statements are NOT executable against the current database.
-- They demonstrate the correct set operation for the business
-- specification below - nothing here was run against real data.
-- =====================================================================
--
-- Day 7 - Task 3: Set operations from a spec
--
-- The real quotes.db (day - 2/QuotesApi/quotes.db) only contains:
--   Quotes(Id, Author, Text, IsDeleted)
--   Collections(Id, Name, OwnerId)
--   CollectionItem(Id, QuoteId, AddedAt, CollectionId)
--   Users(...), RefreshTokens(...)
-- It has no Authors table, no Tags table, no quote/tag relationship,
-- no Categories table, and no "classic"/"modern" representation of any
-- kind. None of the three queries below can run against it.
--
-- Instead, every query below assumes ONE consistent hypothetical
-- schema, used throughout this file only to show the correct SQL SET
-- OPERATION for each business question:
--
--   Authors(Id, Name)
--   Quotes(Id, AuthorId, IsDeleted)
--   Tags(Id, Name)
--   QuoteTags(QuoteId, TagId)              -- a quote can have many tags
--   Categories(Id, Name)                   -- e.g. rows 'Classic', 'Modern'
--   AuthorCategories(AuthorId, CategoryId) -- an author can belong to many categories
--   CategoryTags(CategoryId, TagId)        -- a category can have many tags
--
-- None of these tables exist today. This file is a translation
-- exercise (business question -> correct set operation), not a
-- runnable script.
-- =====================================================================


-- =====================================================================
-- QUESTION 1 - "authors with quotes but no tags"  ->  EXCEPT
-- =====================================================================
-- EXCEPT returns rows in the left result that are NOT present in the
-- right result - exactly "in this set, but not in that one." That is
-- precisely what "authors with quotes BUT NO tags" means: start from
-- authors who have at least one non-deleted quote, then remove any
-- author who has at least one tagged quote. SQLite's EXCEPT already
-- deduplicates its output, so an author with several untagged quotes
-- still appears only once; SELECT DISTINCT is added on each side anyway
-- to make the "one row per author" intent explicit.
SELECT DISTINCT a.Id, a.Name
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
WHERE q.IsDeleted = 0

EXCEPT

SELECT DISTINCT a.Id, a.Name
FROM Authors a
INNER JOIN Quotes q ON q.AuthorId = a.Id
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
WHERE q.IsDeleted = 0;


-- =====================================================================
-- QUESTION 2 - "authors in both the classic and 'modern' sets"  ->  INTERSECT
-- =====================================================================
-- INTERSECT returns only the rows common to BOTH result sets - members
-- of set A that are also members of set B. That is exactly "authors in
-- both Classic and Modern": authors linked to the Categories row named
-- 'Classic', intersected with authors linked to the Categories row
-- named 'Modern'. An author linked to only one of the two categories
-- is correctly excluded by INTERSECT.
SELECT DISTINCT a.Id, a.Name
FROM Authors a
INNER JOIN AuthorCategories ac ON ac.AuthorId = a.Id
INNER JOIN Categories c ON c.Id = ac.CategoryId
WHERE c.Name = 'Classic'

INTERSECT

SELECT DISTINCT a.Id, a.Name
FROM Authors a
INNER JOIN AuthorCategories ac ON ac.AuthorId = a.Id
INNER JOIN Categories c ON c.Id = ac.CategoryId
WHERE c.Name = 'Modern';


-- =====================================================================
-- QUESTION 3 - "the combined distinct tag list across two categories"  ->  UNION
-- =====================================================================
-- UNION combines both result sets AND removes duplicates, which is
-- exactly what "combined DISTINCT tag list" asks for. UNION ALL would
-- be the wrong choice here: if the same tag is linked to both
-- categories, UNION ALL would list that tag name twice, contradicting
-- "distinct." UNION (without ALL) is used deliberately.
SELECT DISTINCT t.Name
FROM Tags t
INNER JOIN CategoryTags ct ON ct.TagId = t.Id
INNER JOIN Categories c ON c.Id = ct.CategoryId
WHERE c.Name = 'Classic'

UNION

SELECT DISTINCT t.Name
FROM Tags t
INNER JOIN CategoryTags ct ON ct.TagId = t.Id
INNER JOIN Categories c ON c.Id = ct.CategoryId
WHERE c.Name = 'Modern';
