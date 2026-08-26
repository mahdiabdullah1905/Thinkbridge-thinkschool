# Day 15 Task 1 — HttpClient + interceptors

Standalone, zoneless Angular 21 app wiring `HttpClient` with three functional interceptors
against the real Week-1 API (`day - 2/QuotesApi`, run with `dotnet run` from that directory —
`http://localhost:5225`). Characterization tests were written and run green against the real,
running API *before* any interceptor or UI code was written.

## The real API contract (verified with curl against the running API, not guessed)

**`GET /api/quotes?page=N&size=N`** → `200`, `Content-Type: application/json`:

```json
{"page":1,"size":2,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}
```

**A real 4xx turned out to have three different shapes depending on how the endpoint
produces it** — this was the main wrong assumption this task caught (see below):

1. **`ValidationProblemDetails`** — from `ValidationFilter<T>` (`DataAnnotations` failures),
   e.g. `POST /api/auth/login` with a bad email:
   ```json
   {"type":"...","title":"One or more validation errors occurred.","status":400,
    "errors":{"Email":["The Email field is not a valid e-mail address."],"Password":["The Password field is required."]},
    "traceId":"00-ac46bf0a288e8c2f79b5106f7d99c5e7-12a1c360517303e6-01"}
   ```
   `Content-Type: application/problem+json`.

2. **Plain `ProblemDetails`**, hand-constructed by a handler, e.g. `POST
   /api/collections/{id}/quotes` when the quote is already in the collection:
   ```json
   {"type":"...","title":"Cannot add quote","status":400,"detail":"Quote 1 is already in the collection."}
   ```
   `Content-Type: application/json; charset=utf-8` — **not** `problem+json`. Content-Type
   cannot be used to distinguish this from case 1; only the presence of an `"errors"` key can.

3. **No body at all** — `401` from an unauthenticated request, `404` from `GET
   /api/quotes/{id}` for a missing id (`Results.Unauthorized()` / `Results.NotFound()`).
   Empty response, no `Content-Type` header, despite `AddProblemDetails()` being registered
   in `Program.cs`.

## Files

```
src/app/
  quotes-api.ts, auth-api.ts        - typed HttpClient services for the real endpoints
  auth-token-store.ts                - in-memory signal holding the access token
  errors/app-error.ts                - the typed AppError union the UI renders
  interceptors/
    auth-interceptor.ts              - attaches Authorization: Bearer <token>
    retry-interceptor.ts             - retries idempotent GETs with backoff
    error-mapping-interceptor.ts     - maps ProblemDetails/ValidationProblemDetails -> AppError
  app.config.ts                      - wires provideHttpClient(withInterceptors([auth, errorMapping, retry]))
  app.ts / app.html / app.css        - minimal UI: quote list + sign-in form, both error-aware

  quotes-api.characterization.spec.ts   - characterization tests, REAL API, no mocks
  error-mapping.integration.spec.ts     - full interceptor chain, REAL API, no mocks
  interceptors/*.spec.ts                - per-interceptor unit tests, HttpTestingController
  app.spec.ts                           - component-level tests through the full chain (mocked backend)
```

## The characterization test

`quotes-api.characterization.spec.ts` uses `provideHttpClient(withFetch())` with **no**
`HttpClientTestingModule` — every request in that file is a real network call to
`http://localhost:5225`. It pins:
- the `GET /api/quotes` shape above,
- the `ValidationProblemDetails` shape from a bad login,
- that `GET /api/quotes/{missingId}` 404s with an empty body,
- that `POST /api/quotes` without a token 401s with an empty body.

This ran green before `app.config.ts`, the interceptors, or the UI existed.

## How each interceptor works

**`authInterceptor`** — reads `AuthTokenStore.currentToken()`; if set, clones the request
with `Authorization: Bearer <token>`. Skips `/api/auth/login` and `/api/auth/refresh`
explicitly, since sending a stale/absent token there is meaningless.

**`retryInterceptor`** — only touches `req.method === 'GET'`; everything else passes through
untouched. Retries up to twice with exponential backoff (200ms, 400ms), but only for
`status === 0` (network) or `status >= 500` — a 4xx means the request itself was wrong, so
retrying it would just repeat the same rejection. It has to sit **closer to the backend**
than `errorMappingInterceptor` in the array passed to `withInterceptors()`, or it would be
retrying already-mapped `AppError`s instead of raw `HttpErrorResponse`s.

**`errorMappingInterceptor`** — catches the final `HttpErrorResponse` (after any retries) and
maps it to a typed `AppError`: `'validation'` (body has an `errors` map), `'problem'` (body
has `title`/`detail`/`status` but no `errors`), `'network'` (status 0), or `'unknown'` (a real
4xx/5xx with no parseable body — covers the 401/404 empty-body case above). Each variant
carries a ready-to-render `message`; nothing downstream parses ProblemDetails itself.

Interceptor order in `app.config.ts`: `[authInterceptor, errorMappingInterceptor,
retryInterceptor]` — auth outermost (attaches the header before anything else runs),
error-mapping in the middle (only converts the *final* post-retry failure), retry innermost
(closest to the backend, sees raw errors to decide on retries).

## What was actually verified

- **Characterization tests, real API, no mocks**: `quotes-api.characterization.spec.ts` (4
  tests) — ran and passed against the live `dotnet run` process.
- **GET retry behavior**: `retry-interceptor.spec.ts` — a GET that 503s once then succeeds is
  retried and resolves; a GET that 503s three times in a row surfaces the final failure after
  exhausting the retry budget (`HttpTestingController`, mocked, so the exact retry count is
  asserted deterministically).
- **POST is not retried**: same file — a POST that 503s is never retried (`httpMock.expectNone`
  after the single failed attempt).
- **4xx is not retried on GET either**: a GET that 400s is not retried (4xx isn't transient).
- **Authorization header**: `auth-interceptor.spec.ts` — header present and correct when a
  token is set, absent when it isn't, and never sent to `/api/auth/login`. Also verified at the
  component level in `app.spec.ts` (header absent on the login POST itself, since that's the
  request that obtains the token).
- **A real 4xx ProblemDetails response reaching the UI as a friendly error**: verified twice —
  (1) `error-mapping.integration.spec.ts` runs the full interceptor chain against the live API
  and asserts the caught error's `message` equals the server's actual validation text, not a
  JSON dump; (2) `app.spec.ts` flushes a real-shaped `ValidationProblemDetails` body through
  the full chain into the rendered component and asserts the friendly message appears in the
  DOM while the raw `type`/`traceId` fields do not leak into it.
- **`ng build`**: production build succeeds (`dist/quotes-http-client`), no TypeScript errors
  under this repo's strict `tsconfig` (`noPropertyAccessFromIndexSignature`, `strict`, etc.).
- **`ng test`**: full suite — 22/22 passing across 6 spec files.
- **Dev server + proxy**: `ng serve` on port 4215 with `proxy.conf.json` (`/api` →
  `http://localhost:5225`) confirmed serving `index.html` and correctly proxying a real `GET
  /api/quotes` request through to the live API.
- **git status**: only `day - 15/` is new; nothing under `day - 2/QuotesApi` (the API) was
  modified. `day - 1`'s modified `obj/` files and the untracked `day - 3/task - 2/README.md`
  predate this session and are unrelated.

## A mistake this caught along the way

**A genuine effect-tracking bug, not just an assumption slip.** The quotes-loading `effect()`
in `app.ts` originally called `this.quotesApi.getQuotes(1, 5).subscribe(...)` directly. Because
`HttpClient` runs interceptors synchronously during `.subscribe()`, and `authInterceptor` reads
`AuthTokenStore.currentToken()` — a signal — that read happened *while the effect's function was
still on the call stack*. Angular's effect dependency tracking is stack-based, not
lexical-scope-based, so it silently attributed that read to the quotes-loading effect as a
dependency, even though the effect's own body never touches the token. The result: every time
`AuthTokenStore`'s token changed (e.g. after a successful sign-in), the effect re-ran and fired
a second, unwanted `GET /api/quotes`.

This wasn't caught by reasoning about the code — it surfaced as a real, reproducible test
failure (`app.spec.ts`'s Authorization-header test kept finding an unflushed extra `GET
/api/quotes` at `httpMock.verify()`), confirmed with an isolated `it.only` run plus a temporary
stack-trace log in the effect. The fix is `untracked()` around the `.subscribe()` call in
`app.ts` — any effect that fires an HTTP request through an interceptor which itself reads a
signal needs this, or it will silently pick up that signal as a dependency.

**A second wrong assumption, caught before it became a bug**: the initial plan was to
distinguish `ProblemDetails` from `ValidationProblemDetails` by `Content-Type`
(`application/problem+json` vs not). Curling the real collections endpoint showed a legitimate,
manually-constructed `ProblemDetails` response served with `Content-Type: application/json` —
so `errorMappingInterceptor` keys off the presence of an `"errors"` object in the body instead,
never the `Content-Type` header.

**A third one**: `CreateQuoteCommandHandler`'s domain-rule `BadRequest(new
ProblemDetails{...})` path looked reachable from the model (`Quote.Create` rejects
whitespace-only author/text), but testing it directly showed `[Required]` on
`CreateQuoteRequest` already rejects whitespace-only strings in this .NET version, so
`ValidationFilter` intercepts first — that plain-`ProblemDetails` branch in the quotes handler
is effectively dead code from the API's own boundary. The real, reachable example of that shape
turned out to be the *collections* "quote already added" path instead, which is what
`error-mapping-interceptor.spec.ts` and `error-mapping.integration.spec.ts` actually exercise.

## What would break if the API's contract changed

- **Field rename in `PaginatedResponse`/`QuoteListItem`** (e.g. `totalCount` → `total`): the
  characterization test fails immediately and visibly, before anything downstream is touched —
  that's the point of pinning it first.
- **`ValidationProblemDetails.errors` renamed or restructured**: `hasFieldErrors()` in
  `error-mapping-interceptor.ts` would stop matching, and every validation failure would fall
  through to the `'problem'` or `'unknown'` branch — messages would get less specific (lose
  per-field detail) but wouldn't crash, since every branch produces a safe fallback message.
- **A currently-empty-body error (401/404) started returning a real `ProblemDetails` body**:
  harmless — it would just be picked up by `looksLikeProblemDetails()` and produce a better
  message than the current generic one.
- **The API stopped setting any recognizable body key at all on a validation error** (e.g.
  switched away from ASP.NET's default validation problem shape): `errorMappingInterceptor`
  would silently downgrade validation failures to generic `'unknown'` messages — this would be
  a silent quality regression, not a crash, and the characterization test would be the only
  thing to catch it (which is exactly why it exists).
- **Retry status-code semantics** (e.g. the API started returning `429` for transient
  overload): `retryInterceptor` currently only retries `0`/`>=500`, so `429`s would never be
  retried under the current rule — a deliberate, narrow definition of "transient" that would
  need revisiting if that changed.

## Note on the running API's data

Verifying the collections `ProblemDetails` shape required creating one real collection and
adding one quote to it via the live API (`POST /api/collections`, `POST
/api/collections/{id}/quotes`) — this went into the local dev `quotes.db`, which is
gitignored (`day - 2/.gitignore`) and confirmed to produce no `git status` changes. No API
source file was modified.
