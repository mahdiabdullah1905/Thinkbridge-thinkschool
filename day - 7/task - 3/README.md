# Day 7 — Task 3: Set operations from a spec

## 1. What this task was teaching

This task hands you a business question in plain English, not SQL, and asks
you to translate it into the right *set operation* — `UNION`, `INTERSECT`,
or `EXCEPT` — and explain why that one is correct. It is a translation
exercise, not an implementation exercise.

## 2. What was actually verified vs. what is illustrative

**Actually verified, against the real repository and the real database:**
the current SQLite schema at `day - 2/QuotesApi/quotes.db` was inspected
directly (`sqlite_master`, all `CREATE TABLE` statements, all EF Core
migrations), and the whole repository was searched for "tag", "tags",
"category", "categories", "classic", and "modern". The result: none of
those concepts exist anywhere in this codebase or database. The real
schema is exactly:

```
Quotes(Id, Author, Text, IsDeleted)
Collections(Id, Name, OwnerId)
CollectionItem(Id, QuoteId, AddedAt, CollectionId)
Users(Id, Email, PasswordHash)
RefreshTokens(Id, TokenHash, UserId, FamilyId, ExpiresAt, RevokedAt, ReplacedByTokenHash)
```

There is no `Authors` table (`Author` is a plain string column on
`Quotes`), no `Tags` table, no quote-to-tag or author-to-tag
relationship, no `Categories` table, and no `classic`/`modern`
representation of any kind — not a column, not a table, not even a value
anywhere in the existing data.

`Collections` and `CollectionItem` **do exist and are real**, but they are
**not** a tags or categories mechanism: `Collections` is a user-owned,
arbitrarily-named list (`Name`, `OwnerId`) that a user curates, and
`CollectionItem` just records which quotes were added to which collection
and when. Nothing about that structure represents "this quote has tag X"
or "this author belongs to the classic/modern category." They are also
currently **empty** in the real `quotes.db` (zero rows in either table),
so even if one squinted at them as a stand-in, there is no real data
today to query. This README does **not** treat `Collections`/
`CollectionItem` as equivalent to tags or categories.

**Illustrative only, not verified against anything real:** the three SQL
statements in `set-operations.sql`. They assume a hypothetical schema
that does not exist in this repository, were never run, and no rows or
results are claimed from them. No tags or categories were implemented as
part of this task — only documented as missing, with the correct SQL
pattern shown for when they might exist.

## 3. Why these queries cannot run against the real `quotes.db`

Each business question requires a concept the real schema doesn't have:

- Question 1 needs a way to know "this author has at least one tagged
  quote" — there is no tag concept at all.
- Question 2 needs a way to know "this author belongs to the classic
  set" / "the modern set" — there is no category/set concept at all.
- Question 3 needs both tags and categories, plus a mapping between
  them — neither exists.

So `set-operations.sql` uses a single, consistent **hypothetical** schema
instead, stated once at the top of that file:

```
Authors(Id, Name)
Quotes(Id, AuthorId, IsDeleted)
Tags(Id, Name)
QuoteTags(QuoteId, TagId)              -- a quote can have many tags
Categories(Id, Name)                   -- e.g. rows 'Classic', 'Modern'
AuthorCategories(AuthorId, CategoryId) -- an author can belong to many categories
CategoryTags(CategoryId, TagId)        -- a category can have many tags
```

None of these tables exist in this repository. If tags/categories were
ever added for real, the minimum missing pieces would be: a `Tags` table
plus a quote-or-author-to-tag join, and some representation of "classic"
vs "modern" (a column, a dedicated table, or — reusing what already
exists — two specifically-named, populated `Collections`). That decision
is out of scope for this task.

## 4. Question 1 — "authors with quotes but no tags" → `EXCEPT`

`EXCEPT` returns rows that are in the left result set but **not** in the
right one — exactly "in this set, but not in that one." The left side is
every author with at least one non-deleted quote; the right side is every
author who has at least one tagged quote (via the hypothetical
`QuoteTags` join). Subtracting the second from the first gives authors
who have quotes but none of them are tagged.

`IsDeleted = 0` is applied on both sides, mirroring the real application's
soft-delete convention (`Quotes.IsDeleted` and its EF Core global query
filter), so a soft-deleted quote never makes an author look like they
"have quotes." SQLite's `EXCEPT` already removes duplicate rows from its
result, so an author with several untagged quotes still appears only
once; `SELECT DISTINCT` is included on each side anyway to make that
one-row-per-author intent explicit rather than relying on it implicitly.

## 5. Question 2 — "authors in both the classic and 'modern' sets" → `INTERSECT`

`INTERSECT` returns only the rows that are members of **both** input sets
— nothing that's in just one side. That is a direct match for "in both
Classic and Modern": authors linked to a `Categories` row named
`'Classic'`, intersected with authors linked to a `Categories` row named
`'Modern'`. An author linked to only one of the two categories is
correctly excluded — that's the whole point of `INTERSECT` versus a plain
`OR`/`UNION`, which would incorrectly include authors in *either* set.

## 6. Question 3 — "the combined distinct tag list across two categories" → `UNION`

`UNION` combines two result sets **and removes duplicates** across them,
which matches "combined **distinct** tag list" precisely. `UNION ALL` was
deliberately not used: if a tag is linked to both categories,
`UNION ALL` would list that tag name twice, directly contradicting the
word "distinct" in the business question. Plain `UNION` is the correct
choice specifically because de-duplication is part of the requirement,
not just a side effect.

## 7. Notes on correctness details considered

- **Duplicates:** each hypothetical query uses `SELECT DISTINCT` before
  the set operation, and `EXCEPT`/`INTERSECT`/`UNION` (without `ALL`) all
  deduplicate their combined output regardless, so an author or tag can
  never appear more than once in any result.
- **NULLs:** `Quotes.IsDeleted` is a required, non-nullable column in the
  real schema, and the hypothetical `Authors.Name`/`Tags.Name` columns
  are assumed non-nullable too, so there's no NULL-matching ambiguity in
  any of the three set operations.
- **Soft-deleted quotes:** excluded via `WHERE q.IsDeleted = 0` everywhere
  `Quotes` is referenced, matching the real application's convention.
- **Column-type matching:** every side of every set operation selects the
  same two columns in the same order and type (`Id`, `Name` for authors;
  `Name` for tags), which SQLite requires for `UNION`/`INTERSECT`/`EXCEPT`
  to be valid.
- **How "categories" would be represented:** the hypothetical schema
  treats a category as a *row* in a `Categories` table (identified by
  `Name = 'Classic'` / `Name = 'Modern'`), linked to authors and tags via
  join tables (`AuthorCategories`, `CategoryTags`) — not as a column value
  directly on `Authors` or `Tags`, since either author or tag could
  plausibly belong to more than one category.

## 8. Summary

| Question | Operation | Why |
|---|---|---|
| Authors with quotes but no tags | `EXCEPT` | "in this set, not in that one" |
| Authors in both classic and modern | `INTERSECT` | "common to both sets" |
| Combined distinct tag list across two categories | `UNION` | "combine and de-duplicate" |

This task did not modify QuotesApi, did not modify or add to
`quotes.db`, did not add any EF Core migration, and did not implement
tags or categories anywhere. It is documentation only, translating three
business questions into the correct SQL set operation and explaining why
the real database can't answer them yet.
