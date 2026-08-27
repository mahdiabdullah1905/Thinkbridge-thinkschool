# Day 16, Task 2 — State management, signals first

A small quotes feature (list + selected detail) whose state lives entirely in
`QuotesStore`: plain Angular signals inside an injectable service, no NgRx,
no `@ngrx/signals`. HTTP stays in `QuotesApi`, unchanged from Day 16 Task 1.
Built against the real Week-1 API (`day - 2/QuotesApi`, `dotnet run`,
`http://localhost:5225`) — no mock data, no invented endpoints, no modified
Week-1 code.

## Angular version actually installed (checked before writing any code)

`@angular/core@21.2.22`. Checked `node_modules/@angular/core/types/core.d.ts`
directly rather than assuming from older tutorials:

- `signal`, `computed`, `linkedSignal`, `untracked` — stable.
- `resource()` — present, but still tagged `@experimental 19.0` in this exact
  installed version. Deliberately **not used** here: the task asked for
  "signals for the feature state" with "HTTP/API work inside a service," and
  `resource()` bundles the fetch and the signal together in a way that would
  have blurred that separation — plus it's still experimental in the version
  actually installed. Plain `signal()`/`computed()` with imperative service
  methods is what's used instead; see `quotes-store.ts`.

## The real API contract (this task reuses, doesn't re-derive)

```
GET /api/quotes?page=1&size=5
{"page":1,"size":5,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}

GET /api/quotes/3
{"id":3,"author":"Test","text":"With token","isDeleted":false}

GET /api/quotes/999999
(empty body, HTTP 404)
```

## Files

```
src/app/
  quotes-api.ts                    - HTTP only: getQuotes, getQuoteById (copied from Day 16 Task 1, unchanged)
  auth-token-store.ts, errors/app-error.ts,
  interceptors/*.ts                - copied unmodified from Day 16 Task 1 (auth/error-mapping/retry chain)
  quotes-store/
    quotes-store.ts (+ .spec.ts)   - THE point of this task: signals + a service
  app.ts / app.html / app.css      - thin view: reads QuotesStore's signals, calls its methods, holds no state itself
  app.config.ts                    - provideZonelessChangeDetection + the same interceptor chain
```

No routing in this task (Task 1 already covers routing/guards/lazy-loading) —
this is a single view exercising the store.

## What state is represented by signals

All inside `QuotesStore`, split into two independent slices - nothing
duplicated between the store and the component:

**List slice**: `page`, `pageSize`, `quotes`, `totalCount`, `listStatus`
(`'idle' | 'loading' | 'loaded' | 'empty' | 'error'`), `listError`.
`computed()`: `totalPages`, `canGoPrevious`, `canGoNext` — pure derivations
from `totalCount`/`pageSize`/`page`, never stored redundantly.

**Detail/selection slice**: `selectedId`, `detail`, `detailStatus`
(`'idle' | 'loading' | 'loaded' | 'notfound' | 'error'`), `detailError`.

All exposed as `.asReadonly()` signals; only `loadPage()`, `selectQuote()`,
and `clearSelection()` can mutate them. `App` (`app.ts`) injects the store
and holds **zero** signals of its own — every one of the "covers at least
loading/loaded/empty/error/selected-detail" states requested lives in the
service, not duplicated into the component the way Day 13's version put
list+detail signals directly on the component.

No `effect()` is used anywhere. `loadPage`/`selectQuote` are called
imperatively (from the constructor for the initial page, from template event
bindings for everything else) and update signals directly inside the
`.subscribe()` callback. This sidesteps the `untracked()` dance Days 13/15/16
Task 1 needed — that was only necessary because those fetches ran inside a
reactive `effect()` that auto-triggered on signal changes. Nothing here
auto-triggers, so there's no tracked-scope-vs-HTTP-call interaction to guard
against. Simpler, and the store spec never has to think about signal
tracking at all — just constructor + method calls.

## Service/API calls used

- `QuotesApi.getQuotes(page, size)` → `GET /api/quotes?page=N&size=N`
- `QuotesApi.getQuoteById(id)` → `GET /api/quotes/{id}`

Both unchanged from Task 1's `quotes-api.ts` — no new endpoint, no new field.

## Tests and verification

```
npx ng test    → 5 spec files, 23 passed
npx ng build   → main-*.js 148.81 kB / 43.60 kB transfer, no errors
```

`quotes-store.spec.ts` (9 tests) is the core of "test the important state
transitions and API behaviour," against `HttpTestingController` +
`errorMappingInterceptor` only (auth/retry are irrelevant to what's being
tested and already covered by their own spec files copied from Task 1):

- constructor loads page 1 and starts `listStatus() === 'loading'`
- a 200 with items → `'loaded'`, a 200 with `items: []` → `'empty'`
- a failed request → `'error'` with the mapped `AppError.message` preserved
- `loadPage(2)` requests the next page with the right `page`/`size` params
- **stale-response guard, list**: `loadPage(2)` then `loadPage(3)` before
  either resolves; flushing page 3 first and the now-stale page 2 second
  still leaves the store showing page 3's data
- `selectQuote(id)` → `'loading'` → `'loaded'` with the real detail shape
- a real 404 (empty body, matching the curl above) → `'notfound'`, not the
  generic `'error'`
- **stale-response guard, detail**: same race as above, for
  `selectQuote(1)` then `selectQuote(2)`
- `clearSelection()` resets `selectedId`/`detail`/`detailStatus` to idle/null

`app.spec.ts` (2 tests) confirms the component wiring: renders the store's
loading/loaded states, and a real click calls `selectQuote` and renders the
resulting detail.

**Exercised against the live API** (`ng serve` + real `dotnet run`, driven
with headless Chromium since no GUI is available in this environment — same
approach as Task 1's verification):

- List renders 5 real items from `GET /api/quotes?page=1&size=5`.
- Clicking the second quote (`id=3`, author `Test`) fires `GET
  /api/quotes/3` and the detail panel's author matches the list row's
  author exactly.
- Clicking "Next" fires `GET /api/quotes?page=2&size=5` and the pager
  correctly shows "Page 2 of 2" (`totalCount=6`, `pageSize=5`).

The real 404 path (`GET /api/quotes/999999`) is verified by the unit test
above rather than by clicking through the browser: this UI has no input for
an arbitrary id (by design - "keep the state simple," no extra text box
that isn't otherwise needed), so it only ever requests ids that came from
the real list. The empty-body/404 shape itself was already confirmed by curl
in Task 1 against this same endpoint; the test asserts the store maps it to
`'notfound'` rather than the generic `'error'`.

## A mistake caught while reviewing my own diff

`auth-api.ts` (the `AuthApi.login()` service) was copied over from Task 1's
scaffolding along with `auth-token-store.ts` and the interceptors, then never
used anywhere: this task has no login UI and no code ever calls `.login()`.
Grepping the new source tree for `AuthApi` turned up exactly one match — its
own class declaration — confirming it was dead weight, not a dependency of
anything. Deleted it.

`AuthTokenStore` and `authInterceptor` were kept, deliberately: `authInterceptor`
genuinely reads `AuthTokenStore.currentToken()` inside the real HTTP chain
wired up in `app.config.ts`, so removing *those* would mean hand-rolling a
thinner interceptor chain instead of reusing Task 1's — the opposite of what
"reuse where appropriate" asked for. The distinction is "does anything call
this" (no, for `AuthApi`) vs. "is this wired into a chain that's actually
used" (yes, for the token store/interceptor) - both true `providedIn: 'root'`
services, only one of them actually reachable.

## When this would actually need signal-store/NgRx (my judgment call, not the agent's)

This stays a plain-signals service as long as three things hold: (1) one
service owns state that only one feature area reads, (2) state transitions
are simple enough to write by hand as a handful of `.set()` calls without
needing selectors, effects-with-cleanup, or entity normalization, and (3)
nothing outside this feature needs to react to its state changes.

I'd reach for `@ngrx/signals` (not full NgRx-with-actions, which is a bigger
jump again) at the point where **any one** of these starts being true:

- **Cross-feature sharing with independent write access.** If a second,
  unrelated feature (say, a "recently viewed quotes" widget in a totally
  different part of the app) needed to both read *and* write into the same
  quotes state, a plain injected service still technically works, but you
  lose the tooling (devtools time-travel, structured update tracing) that
  makes multiple writers into shared state debuggable. One writer, many
  readers - like this store today - doesn't need that yet.
- **The state shape stops being "a couple of lists and a selection."** Once
  you're normalizing entities (e.g. quotes referenced from multiple
  collections, needing a single normalized `Record<id, Quote>` so an edit
  in one place is visible everywhere), hand-writing that normalization and
  its update logic in a plain service is exactly the boilerplate
  `signalStore`'s entity helpers exist to remove.
- **The number of interdependent derived signals grows past a handful.**
  Right now there are 3 `computed()`s, each depending on 1-2 signals. If
  this feature grew to where changing one signal had to correctly cascade
  through 8-10 interdependent `computed()`s, the store class itself becomes
  the hard-to-review part, and `signalStore`'s more declarative
  `withComputed`/`withMethods` composition starts paying for itself.
- **Optimistic updates with rollback, or undo/redo.** Nothing here mutates
  anything (both endpoints used are `GET`s) - the moment a mutation needs
  "update the UI immediately, roll back if the request fails," that's
  exactly the kind of state-machine logic that's easy to get subtly wrong by
  hand and is what `signalStore`'s update patterns are built to make safe.

None of those are true here: one feature, one reader (the component), two
`GET`s, two independent slices, no cross-cutting writers. Introducing
`signalStore` today would mean adopting its API surface (`withState`,
`withComputed`, `withMethods`, `patchState`) to express something a dozen
lines of `signal()`/`computed()` already expresses clearly - the "don't
introduce it just because it's available" instruction in the brief is the
right call for this feature as it stands.

## What would break if the real API changed

- **`GET /api/quotes` renaming a field** (e.g. `totalCount` → `total`):
  `store.totalCount()` would silently read `undefined`; `totalPages`
  becomes `NaN`-driven (`Math.ceil(undefined / 5)` → `NaN`, then
  `Math.max(1, NaN)` → `NaN`), and the pager would render "Page 1 of NaN."
  Nothing throws - it just silently degrades. The characterization-style
  curl checks in this README are what would catch this before it shipped,
  not a runtime guard, since none exists.
- **`GET /api/quotes/{id}` renaming `text`/`author`/`isDeleted`**: the
  detail panel would render `undefined` where the missing field should be;
  same silent-degradation risk, since `getQuoteById`'s response is trusted
  at the TypeScript type level only (`http.get<QuoteDetail>(...)`), not
  validated at runtime.
- **The 404-for-missing-id becoming a different shape** (e.g. a real
  `ProblemDetails` body instead of empty): would actually be picked up
  fine - `errorMappingInterceptor`'s `looksLikeProblemDetails()` would match
  it and produce a message from `detail`/`title` instead of the generic
  fallback; `quotes-store.ts`'s `err.status === 404` check for `'notfound'`
  doesn't depend on the body shape at all, only the status code.
- **The endpoint path itself changing** (e.g. `/api/quotes/{id}` →
  `/api/quotes/detail/{id}`): would be a compile-time-safe but
  runtime-visible break - `getQuoteById` would 404 against the *old* path
  for every id, and every `selectQuote()` call would land in `'notfound'`
  even for real ids. The store's own `'notfound'` vs `'error'` split would
  actually mask this somewhat (a real routing bug would look identical to
  "that quote doesn't exist" in the UI) - worth flagging as the one case
  where this task's error handling could hide a real bug instead of a real
  absence.
