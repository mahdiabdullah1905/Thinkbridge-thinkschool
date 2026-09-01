# Day 12 — Task 1: Read models + CQRS-lite

## Feature selected

`GET /api/quotes` (list) and `POST /api/quotes` (create), in
`day - 2/QuotesApi`. Reused rather than invented, because it's the one
feature in this API where the write shape and the ideal read shape are
already visibly different once you look past "they're both about a
`Quote`":

- **Write side** cares about one row at a time: is this author/text pair
  valid, and can it be persisted. That's exactly what the `Quote` entity
  already models (`models/Quotes.cs`) — normalized, one row per quote,
  validated through `Quote.Create`.
- **Read side**, for a list screen, wants two things `Quote` doesn't have:
  a bounded preview instead of up to 1000 characters of `Text` (so the
  list stays cheap to render), and a per-author quote count (so the screen
  can show "Maya Angelou · 2 quotes" next to each row without a second
  round trip). Neither belongs on the write model — `AuthorQuoteCount`
  isn't a fact about a single quote, it's a fact about all of an author's
  quotes, and if it were stored on `Quote` it would need to be rewritten
  on every unrelated insert/delete by that author.

That's the split this task asks for, already sitting in the existing code
under two endpoints that happened to share one repository method
(`IQuoteRepository.GetQuotesAsync`) and one inline handler. No new domain
was invented.

## What existed before

- `MapQuoteEndpoints` (`Extensions/ProgramExtensions.cs`) had the create
  and list logic written directly inline in the minimal-API lambdas —
  validation, persistence, and response shaping all in one place, calling
  `IQuoteRepository` directly.
- `GET /api/quotes` returned `PaginatedResponse<Quote>` — the full
  write-model entity, serialized straight out to the wire.
- No MediatR, no command/query types anywhere in the repo (checked with
  `grep -r MediatR` before adding it — nothing).

## 1. Write model — `CreateQuoteCommand`

`Commands/CreateQuoteCommand.cs` + `Commands/CreateQuoteCommandHandler.cs`:

```csharp
public record CreateQuoteCommand(string Author, string Text) : IRequest<Result<Quote>>;

public class CreateQuoteCommandHandler : IRequestHandler<CreateQuoteCommand, Result<Quote>>
{
    public async Task<Result<Quote>> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var result = Quote.Create(request.Author, request.Text); // same normalized validation as before
        if (!result.IsSuccess) return result;

        await _repository.AddQuoteAsync(result.Value!, cancellationToken); // same repository as before
        return result;
    }
}
```

Nothing about validation or persistence changed — `Quote.Create`'s rules
(author/text required, length caps) and `IQuoteRepository.AddQuoteAsync`
are untouched. The command handler is just where that logic now lives
instead of inline in the endpoint lambda.

`MapQuoteEndpoints`'s `POST /` now dispatches through `ISender` instead of
calling the repository directly:

```csharp
var result = await sender.Send(new CreateQuoteCommand(request.Author, request.Text), ct);
```

(`Extensions/ProgramExtensions.cs:86-101`)

## 2. Read model — `GetQuoteListQuery` → `QuoteListItem`

`models/QuoteListItem.cs`:

```csharp
public record QuoteListItem(int Id, string Author, string TextPreview, int AuthorQuoteCount);
```

`Queries/GetQuoteListQuery.cs` + `Queries/GetQuoteListQueryHandler.cs`
go straight at `AppDbContext` (no repository indirection — the read side
has no reason to route through a type built for tracked, single-row
writes) and project directly to the read model:

```csharp
var items = await _context.Quotes
    .OrderBy(q => q.Id)
    .Skip((request.Page - 1) * request.Size)
    .Take(request.Size)
    .Select(q => new QuoteListItem(
        q.Id,
        q.Author,
        q.Text.Length <= PreviewLength ? q.Text : q.Text.Substring(0, PreviewLength) + "...",
        _context.Quotes.Count(other => other.Author == q.Author)))
    .ToListAsync(cancellationToken);
```

`MapQuoteEndpoints`'s `GET /` now dispatches the query instead of calling
`IQuoteRepository.GetQuotesAsync`:

```csharp
var response = await sender.Send(new GetQuoteListQuery(p, s), ct);
return Results.Ok(response);
```

(`Extensions/ProgramExtensions.cs:76-83`)

`GET /{id}` and `DELETE /{id}` were left exactly as they were — this task
asked for one feature, and those two endpoints already return/act on a
single normalized `Quote`, where there's no read/write shape mismatch to
split.

### Checking the projection actually runs server-side

The correlated `Count(other => other.Author == q.Author)` inside a
`Select` is the kind of thing EF Core sometimes can't translate and
silently falls back to client evaluation for (Day 10 Task 2 hit exactly
that failure mode with `Text.Split(' ').Length`, in this same codebase).
Rather than assume it translated because the tests passed, a throwaway
test enabled `LogTo` with sensitive-data logging against an in-memory
SQLite context seeded with 2 quotes from "Maya Angelou" and 1 from "Rumi",
ran the handler, and printed the actual SQL EF Core sent:

```sql
SELECT "q"."Id", "q"."Author", CASE
    WHEN length("q"."Text") <= 120 THEN "q"."Text"
    ELSE substr("q"."Text", 0 + 1, 120) || '...'
END, (
    SELECT COUNT(*)
    FROM "Quotes" AS "q0"
    WHERE NOT ("q0"."IsDeleted") AND "q0"."Author" = "q"."Author")
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted")
ORDER BY "q"."Id"
LIMIT @p1 OFFSET @p
```

One statement, a scalar `COUNT(*)` subquery correlated on `Author`,
entirely server-side — no N+1, no client-evaluated fallback. The
throwaway test that captured this (`TempSqlCheck.cs`) isn't part of the
final diff; it was only there to look at the generated SQL and got
deleted once it confirmed this.

### Checking the real endpoints, not just the handlers in isolation

Ran the actual app (`Testing` environment, so the JWT key comes from
`appsettings.Testing.json` instead of User Secrets, against a fresh
scratch SQLite file) and hit it with `curl`:

```
POST /api/quotes  {"author":"Maya Angelou","text":"If you are always trying to be normal..."}
→ {"id":1,"author":"Maya Angelou","text":"If you are always trying to be normal you will never know how amazing you can be.","isDeleted":false}

POST /api/quotes  {"author":"Maya Angelou","text":"There is no greater agony..."}
→ {"id":2,"author":"Maya Angelou","text":"There is no greater agony than bearing an untold story inside you.","isDeleted":false}

POST /api/quotes  {"author":"Rumi","text":"The wound is the place where the light enters you... [147 chars]"}
→ {"id":3,"author":"Rumi","text":"The wound is the place where the light enters you, and it is a truth worth remembering every single day of your ordinary, extraordinary life.","isDeleted":false}

GET /api/quotes?page=1&size=10
→ {"page":1,"size":10,"totalCount":3,"items":[
     {"id":1,"author":"Maya Angelou","textPreview":"If you are always trying to be normal you will never know how amazing you can be.","authorQuoteCount":2},
     {"id":2,"author":"Maya Angelou","textPreview":"There is no greater agony than bearing an untold story inside you.","authorQuoteCount":2},
     {"id":3,"author":"Rumi","textPreview":"The wound is the place where the light enters you, and it is a truth worth remembering every single day of your ordinary...","authorQuoteCount":1}
   ]}

POST /api/quotes  {"author":"","text":"no author"}
→ 400 {"errors":{"Author":["The Author field is required."]}}
```

The write response is the normalized `Quote` shape (`id`, `author`,
`text`, `isDeleted`); the read response is the denormalized
`QuoteListItem` shape (`textPreview`, `authorQuoteCount`) — visibly two
different DTOs for the same underlying rows, not the same class read back
with different field names. `authorQuoteCount` is `2` for both Maya
Angelou rows and `1` for the Rumi row, matching what was actually
inserted. The Rumi quote (147 chars) got truncated to 120 chars + `...`
in `textPreview`; the two shorter quotes came through unchanged. The
empty-author `POST` still gets rejected the same way it always did — the
existing `ValidationFilter<CreateQuoteRequest>` runs before the command is
even dispatched, so write-side validation didn't move or weaken.

## 3. Wiring

`AddInfrastructure` (`Extensions/ProgramExtensions.cs:63`) adds one line:

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
```

`MediatR` (14.2.0) was added as a package reference to `QuotesApi.csproj`
— the only new dependency this task needed. No message broker, no event
store, no generic repository/bus abstraction on top of it: `ISender.Send`
is called directly from the two endpoint lambdas that needed it, the same
way `IQuoteRepository` was called directly before.

## Tests

`QuotesApi.Tests/CreateQuoteCommandHandlerTests.cs` (write path, against a
real in-memory SQLite `AppDbContext`, same pattern as the existing
`QuoteRepositoryTests`):
- valid command persists the quote and returns it
- empty author fails validation and nothing is written
- text over 1000 chars fails validation and nothing is written

`QuotesApi.Tests/GetQuoteListQueryHandlerTests.cs` (read path):
- `AuthorQuoteCount` reflects every quote by that author, not just the
  ones on the current page
- text over 120 chars is truncated to 120 + `...`
- shorter text passes through untouched
- `page`/`size`/`totalCount` are respected and accurate across a 5-row,
  page-size-2 split

```
dotnet test day - 2/QuotesApi.Tests

Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 5 s - QuotesApi.Tests.dll (net10.0)
```

36 total = the pre-existing 29 plus these 7 new ones; nothing else in the
suite broke (no other test hits `/api/quotes`, and `GetById`/`Delete`
weren't touched).

## Files

| File | What it is |
|---|---|
| `Commands/CreateQuoteCommand.cs`, `Commands/CreateQuoteCommandHandler.cs` | Write path: command + handler |
| `Queries/GetQuoteListQuery.cs`, `Queries/GetQuoteListQueryHandler.cs` | Read path: query + handler |
| `models/QuoteListItem.cs` | Denormalized read model for the quote-list screen |
| `Extensions/ProgramExtensions.cs` | `POST /`/`GET /` now dispatch through `ISender`; `AddMediatR` registered |
| `QuotesApi.csproj` | `MediatR` 14.2.0 package reference added |
| `QuotesApi.Tests/CreateQuoteCommandHandlerTests.cs` | New tests, write path |
| `QuotesApi.Tests/GetQuoteListQueryHandlerTests.cs` | New tests, read path |

All under `day - 2/QuotesApi` / `day - 2/QuotesApi.Tests` — no new
project, database, or service. `GetById`, `Delete`, auth, and collections
endpoints are unchanged.

## Reproducing

```powershell
cd "day - 2/QuotesApi.Tests"
dotnet test

# to see the real endpoints:
cd "../QuotesApi"
$env:ASPNETCORE_ENVIRONMENT = "Testing"
$env:ConnectionStrings__DefaultConnection = "Data Source=$env:TEMP\quotes-day12.db"
dotnet run -c Release --no-launch-profile --urls http://localhost:5299
```

## Limitations

- `AuthorQuoteCount` is computed fresh on every read (one correlated
  subquery per row, confirmed above to be one round trip regardless of
  page size) rather than maintained as a running total — deliberate for
  "CQRS-lite" with no event sourcing: there's no projection table to keep
  in sync, so there's nothing that can drift out of date, at the cost of
  the database doing a count instead of a lookup on every list request.
  At this table's size that cost is negligible; it isn't re-measured here
  since Day 11 Task 2 already established where the real cost in this
  endpoint's neighborhood comes from (N+1s and missing indexes), and this
  task didn't introduce either.
- `TextPreview`'s 120-character cutoff is arbitrary (chosen to be shorter
  than the shortest quote seeded above yet long enough to read as a real
  preview) — no screen mockup exists in this repo to size it against.
