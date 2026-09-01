# Day 13, Task 2: Quotes list + detail, against the real API

## What was built

A standalone Angular 21 app (zoneless, no NgModule) with a list/detail
screen: the same paginated quote list as Task 1 on the left, and a detail
panel on the right that loads the full quote when you click one. Both
panels talk to the real API in `day - 2/QuotesApi` — no mock data,
no fake endpoints.

Endpoints, confirmed by curl before writing any Angular code (not
guessed):

```
curl "http://localhost:5225/api/quotes?page=1&size=3"
{"page":1,"size":3,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}

curl "http://localhost:5225/api/quotes/1"
{"id":1,"author":"Authorized","text":"With token","isDeleted":false}

curl "http://localhost:5225/api/quotes/999999"
(empty body, HTTP 404)
```

`GET /api/quotes/{id}` returns the full `Quote` entity — `id`, `author`,
`text`, `isDeleted` — not a `QuoteListItem`. It's a different shape from
the list row on purpose (`QuotesApi.Models.Quote` vs `QuoteListItem`),
and `src/app/quotes-api.ts` models them as two separate interfaces
instead of pretending they're the same thing.

## Signals

All in `src/app/app.ts`, split into list state and detail state:

- List: `page`, `pageSize`, `quotes`, `totalCount`, `listLoading`,
  `listError`.
- Detail: `selectedId`, `detail`, `detailLoading`, `detailError`.
- `computed()`: `totalPages` (from `totalCount` + `pageSize`),
  `canGoPrevious`, `canGoNext`, `listStatus` (from three list signals),
  `detailStatus` (from three detail signals). All pure derivations, no
  duplicated state.
- `effect()`: two, one per request. Each is a genuine side effect (an
  HTTP call), which is why it's not a `computed()`.

## The actual race this task is about

Clicking a second quote before the first one's detail response has come
back must not let the first (now stale) response overwrite the second
(current) selection. The detail effect guards this the same way the
list effect guards page changes — by capturing the id the request was
made for, and checking it against the *current* `selectedId()` before
applying the response:

```ts
this.quotesApi.getQuoteById(requestedId).subscribe({
  next: (response) => {
    if (this.selectedId() !== requestedId) return; // a newer selection arrived meanwhile
    this.detail.set(response);
    this.detailLoading.set(false);
  },
  ...
});
```

This is proven with a real, deterministic test rather than trusted on
sight — `src/app/app.spec.ts`, `'ignores a stale detail response when a
newer quote was selected before it resolved'`. It selects quote 1, lets
that request go out, selects quote 2 before flushing anything, then
resolves the mocked responses **out of order** (quote 2's response
first, quote 1's stale one second) and asserts the screen still shows
quote 2. A live network is too fast locally to reliably reorder two
requests on demand, so a controlled test is the honest way to prove
this rather than an unreliable live click sequence.

## Control flow / DI

Same pattern as Task 1: `@switch` for `listStatus()`/`detailStatus()`,
`@for (quote of quotes(); track quote.id)`, `@if` for the pager and the
"deleted" flag. `inject()` for `HttpClient` (in the service) and
`QuotesApi` (in the component) — no constructors take dependencies as
parameters anywhere.

## Verification

```
dotnet build                                       # 0 errors
dotnet run --urls http://localhost:5225

npx ng build       # Application bundle generation complete, 145.6 kB / 42.6 kB transfer
npx ng test        # 7 passed (7)
```

Exercised against the real dev server (`ng serve`, proxying to the real
API, no CORS changes to `day - 2`):

- **Loading → list data**: real first page renders (`Authorized`,
  `Test`, `Mahatma Gandhi`, ...).
- **Select → detail data**: clicking a quote fires a real
  `GET /api/quotes/{id}` and renders its full `text`.
- **Switching selection**: clicking a second quote replaces the detail
  panel with the second quote's real data.
- **Empty list**: typing `9999` into "Go to page" is a structurally
  valid request the API answers with `items: []` — no data was altered
  to fake this. The already-loaded detail panel is unaffected, since
  list and detail state are independent signals.
- **List error**: killed the real `dotnet run` process, reloaded — the
  list showed "Could not reach the quotes API. Is it running?" from a
  genuine failed request. Restarted the API and confirmed 200 again.
- **Detail error, independent of the list**: with the list already
  loaded successfully, killed the API *then* selected a quote — the
  list stayed exactly as it was (already-loaded data, not touched) and
  only the detail panel showed its own error. Screenshot:
  `t2-06-detail-error.png` in the verification run.
- **Stale-response race**: proven with the controlled unit test above
  (7th test in the suite).
- **Console**: no uncaught errors in any of the above, only the one
  expected "failed to load resource: 500" log during each intentional
  outage.

## The mistake I caught reviewing my own diff

Both `.subscribe({ error: ... })` handlers were originally written as
`error: () => { ... }` — the actual error object was never captured at
all. Two real problems with that, not one:

1. **Swallowed error.** If the real API returned a genuine 500 (a server
   bug, not just being offline), the app would show the exact same
   generic message as "server unreachable," and nothing about the real
   failure would even reach the browser console. There was no way for
   whoever's debugging this to tell those two situations apart.
2. **An implicit `any`, without ever typing the word "any."** RxJS's own
   `Observer` interface types the `error` callback parameter as
   `(err: any) => void`. Had I gone back and added `err` as a parameter
   without an explicit type — the natural next step to fix problem 1 —
   TypeScript would have silently accepted `any` from RxJS's typings.
   Grepping the diff for the literal string `any` would have found
   nothing, but the type would still have been there.

**Fix**: both handlers now take `err: HttpErrorResponse` (imported from
`@angular/common/http`), log it with `console.error`, and — for the
detail request specifically — check `err.status === 404` to show "That
quote no longer exists." instead of the generic unreachable-API message,
since `GET /api/quotes/{id}` genuinely 404s for a missing id (confirmed
by curl above) and that's a different situation from the server being
down. Added a dedicated test, `'shows a not-found message specifically
when the detail request 404s'`, which flushes a real `404` status
through `HttpTestingController` and asserts the specific message — it
would have failed against the original code, which showed the generic
message for every error including a 404.

This particular UI never actually triggers the 404 branch live, since
it only ever requests ids that came from the real list response — the
unit test is what actually exercises that path; documented here rather
than left silently unverified.

## What would break

Same limitation as Task 1, doubled: neither the list effect nor the
detail effect actually cancels its in-flight HTTP request when a newer
one supersedes it — both just ignore the result if it arrives late.
Every click still costs a real request to the API even if its result is
thrown away. Also, `getQuoteById`'s response is trusted at the type
level only (`http.get<QuoteDetail>(...)`), the same as Task 1's list
response — if the API renamed `text` to something else, nothing would
throw, the detail panel would just render `"undefined"` where the quote
text should be.
