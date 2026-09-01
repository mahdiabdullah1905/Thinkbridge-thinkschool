# Day 16, Task 2 — State management, signals first

A quotes feature — list, detail, and create — whose data lives in
`QuotesStore`: plain Angular signals inside an injectable service, no NgRx,
no `@ngrx/signals`. Routes mirror the backend's own resource grouping, and
"add a quote" is gated behind sign-in because the backend genuinely requires
it. Built against the real Week-1 API (`day - 2/QuotesApi`, `dotnet run`,
`http://localhost:5225`) — no mock data, no invented endpoints, no modified
Week-1 code.

This is an iteration on an earlier version of this task that had list+detail
as a single page with no routing and no create form. That version's README
content below has been updated in place rather than kept as a separate
section, since several of its claims (e.g. "no `effect()` is used anywhere")
stopped being true once routing was added.

## Angular version actually installed (checked before writing any code)

`@angular/core@21.2.22`. Checked `node_modules/@angular/core/types/core.d.ts`
directly rather than assuming from older tutorials:

- `signal`, `computed`, `linkedSignal`, `untracked` — stable.
- `resource()` — present, but still tagged `@experimental 19.0` in this exact
  installed version. Not used, for the same reason as before: it bundles the
  fetch and the signal together in a way that would blur the "signals for
  state, HTTP in a service" split this task asks for.
- `@angular/forms/signals` (Signal Forms, seen in Day 14 Task 2's create-quote
  rebuild) — also experimental. This is why the create-quote form merged in
  below is based on Day 14 **Task 1**'s stable `ReactiveFormsModule` version,
  not Task 2's Signal Forms version.

## The real API contract (reused, not re-derived)

```
GET /api/quotes?page=1&size=5
{"page":1,"size":5,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}

GET /api/quotes/3
{"id":3,"author":"Test","text":"With token","isDeleted":false}

GET /api/quotes/999999
(empty body, HTTP 404)

POST /api/quotes  { "author": "...", "text": "..." }  ->  201, the created Quote
  - requires a Bearer token (.RequireAuthorization() in ProgramExtensions.cs) -
    an anonymous POST 401s with an empty body before reaching the handler.
```

## Layering / folder structure

```
src/app/
  core/                             - infrastructure, nothing feature-specific
    quotes-api.ts                     HTTP only: getQuotes, getQuoteById, createQuote
    auth-api.ts                       HTTP only: login
    auth-token-store.ts                in-memory signal holding the access token
    errors/app-error.ts                the typed AppError union every error handler receives
    interceptors/*.ts (+ .spec)         auth / error-mapping / retry chain
    auth/auth.guard.ts (+ .spec)       CanActivateFn: redirect to /login when signed out
  state/
    quotes-store/quotes-store.ts (+.spec) - THE point of this task: signals + a service
  features/                          - routed, user-facing; each depends on core/ and state/, never on a sibling feature
    quote-list/        - eager (index route)
    quote-detail/       - lazy
    create-quote/        - lazy, guarded (+.spec, +.validators.ts)
    login/               - lazy
  app.routes.ts, app.config.ts       - wiring
  app.ts / app.html / app.css        - thin shell: nav + <router-outlet/>, no state of its own
```

The dependency direction is one-way: `features/*` → `state/` → `core/`.
Nothing in `core/` imports from `state/` or `features/`, and nothing in
`state/` imports from `features/`. `quotes-store.spec.ts` and
`auth.guard.spec.ts` both prove this in practice — they instantiate the store
and the guard with `HttpTestingController` and never touch a component.

## Routes — mirroring the backend's own grouping

The backend groups its quote endpoints under one route prefix
(`day - 2/QuotesApi/Extensions/ProgramExtensions.cs`,
`app.MapGroup("/api/quotes")` in `MapQuoteEndpoints`). `app.routes.ts` mirrors
that same index/new/show shape client-side:

| Backend | Frontend route | Component | Loaded |
|---|---|---|---|
| `GET /api/quotes` | `quotes` | `QuoteList` | eager (index route) |
| `POST /api/quotes` | `quotes/new` | `CreateQuote` | lazy, **guarded** |
| `GET /api/quotes/{id}` | `quotes/:id` | `QuoteDetail` | lazy |

`quotes/new` is registered before `quotes/:id` in the route array so the
router doesn't try to resolve the literal string `"new"` as an `:id` first.

**The guard here is different from Day 16 Task 1's.** Task 1's guard gated
`quotes/:id` even though the real `GET /api/quotes/{id}` endpoint has no
`.RequireAuthorization()` — that guard was a purely client-side design
choice, documented as such. This time, `quotes/new` maps to `POST
/api/quotes`, which genuinely does carry `.RequireAuthorization()` — an
anonymous POST really does 401 at the API. So this guard mirrors a real
backend rule instead of inventing one; `core/auth/auth.guard.ts` says this in
its own comment, not just here.

## What state is represented by signals

All inside `QuotesStore`, three independent slices, nothing duplicated
between the store and any component:

- **List**: `page`, `pageSize`, `quotes`, `totalCount`, `listStatus`
  (`'idle' | 'loading' | 'loaded' | 'empty' | 'error'`), `listError`, plus
  `computed()` derivations `totalPages`/`canGoPrevious`/`canGoNext`.
- **Detail/selection**: `selectedId`, `detail`, `detailStatus`
  (`'idle' | 'loading' | 'loaded' | 'notfound' | 'error'`), `detailError`.
- **Creation** is deliberately *not* a fourth signal slice — see below.

All exposed as `.asReadonly()` signals; only `loadPage()`, `selectQuote()`,
`clearSelection()`, and `createQuote()` mutate them. Every routed component
(`QuoteList`, `QuoteDetail`, `CreateQuote`) injects the store and holds no
list/detail state of its own.

**Where `effect()` re-entered the picture.** The original single-page version
of this task had no `effect()` at all — `loadPage`/`selectQuote` were called
imperatively and that was true and worth calling out at the time. Routing
changes that: Angular's router **reuses** a component instance across
`/quotes/1` → `/quotes/2` navigations (same route config), so a component
that only calls `store.selectQuote(id)` once in its constructor would never
refetch on the second navigation. `features/quote-detail/quote-detail.ts`
uses `effect()` reacting to the `:id` route-param input, wrapped in
`untracked()` for the same reason Days 13/15/16-Task-1 needed it: the auth
interceptor reads `AuthTokenStore`'s token signal synchronously during
`.subscribe()`, and without `untracked()` that read gets attributed to the
effect as a dependency, causing an extra unwanted refetch on every sign-in/out.
`loadPage`/`selectQuote`/`createQuote` themselves are still plain imperative
methods on the store — the `effect()` lives only in the one place that
genuinely needs reactivity (a reused component reacting to a changing input),
not in the store.

## Why `createQuote()` returns an Observable instead of adding a third status signal

`QuotesStore` could have grown a `createStatus`/`createError` pair, parallel
to the list/detail slices. Deliberately didn't:

- List/detail status is read continuously by templates across renders
  (`@switch` on `listStatus()`), which is exactly what a signal is for.
- A form submission's outcome is a **one-shot** reaction — reset the form,
  move focus, show a success link — needed by exactly one component, once,
  right after the call. That's what `Observable.subscribe({ next, error })`
  already models; adding a signal that only one caller ever reads once would
  be state for its own sake.

So `QuotesStore.createQuote(request)` returns
`this.quotesApi.createQuote(request).pipe(tap(() => this.loadPage(this._page())))`
— the store still owns the one thing only it can do (refresh the list so a
new quote shows up), but the form-submission UX (validation, focus,
success/error rendering) stays in `CreateQuote`, the same split Day 14's
original component already had between "form-local" and "server response"
concerns.

## The create-quote merge from Day 14

Day 14 Task 1's `CreateQuote` (`day - 14/task - 1/src/app/create-quote/`) had
its own `CreateQuoteApi` with a bespoke `CreateQuoteFailure` union
(`fieldErrors | unauthorized | serverMessage | network`), built by a local
`catchError` inside that service. That's a second, parallel error-mapping
scheme sitting next to the one this app's interceptor chain already
produces (`AppError`, in `core/errors/app-error.ts`). Rather than carry both:

- `QuotesApi.createQuote()` is a plain `http.post` — no local `catchError`.
  `errorMappingInterceptor` (already in the chain) maps whatever comes back
  into an `AppError`, same as every other request in this app.
- `AppError`'s existing variants already cover all four of
  `CreateQuoteFailure`'s cases: `'validation'` (has `fieldErrors`) for a 400
  with field errors, `'unknown'` for the empty-body 401, `'problem'` for a
  hand-constructed `ProblemDetails` rejection, `'network'` for status 0.
  Nothing new was needed in `app-error.ts`.
- The one piece of real logic Day 14 had that's specific to *this* endpoint —
  `ValidationProblemDetails.errors` keys the field by the C# property name
  (`"Author"`, not `"author"`) — is preserved as `fieldMessage()` in
  `create-quote.ts`, matching case-insensitively against the form's
  lowercase `formControlName`s.
- The accessible form itself (labelled inputs, `aria-invalid`/
  `aria-describedby`, focus-to-first-invalid-field, `role="alert"`/
  `role="status"`) is carried over essentially unchanged — that part had
  nothing to do with error-mapping duplication and was worth keeping as-is.

Day 14 Task 2's Signal-Forms rebuild was **not** the base for this merge —
see the Angular-version note above.

## Tests and verification

```
npx ng test    → 6 spec files, 33 passed
npx ng build   → main-*.js 6.52 kB; lazy chunks: create-quote (6.32 kB),
                 login (2.37 kB), quote-detail (2.01 kB) - quote-list stays
                 in the initial bundle as the index route
```

`quotes-store.spec.ts` (11 tests): the original 9 (construction/loading,
empty, error, `loadPage` params, stale-response guards for both list and
detail, `selectQuote`, real-404 → `'notfound'`, `clearSelection`) plus 2 new
ones for `createQuote()`:

- posts the request, emits the created quote, and triggers exactly one
  follow-up `GET /api/quotes` (the list refresh) — proving the store's own
  side effect actually fires, not just that the POST succeeds
- a failed POST propagates a mapped `AppError` (asserted as `kind ===
  'validation'`) and does **not** trigger a list refresh

`create-quote.spec.ts` (8 tests, adapted from Day 14's spec onto the new
`QuotesStore`/`AppError`-based version): accessible empty state, client-side
required/blank/max-length validation with focus management, a real POST
success (asserting the success message *and* the resulting list-refresh
`GET`), a real 401 (empty body) rendering a sign-in message, a real
field-validation 400 focusing the right control, and a network failure
rendering a distinct message. One assertion in the ported test was actually
wrong on first run — see below.

`auth.guard.spec.ts` (2 tests): redirects to `/login?returnUrl=%2Fquotes%2Fnew`
when signed out, allows activation when a token is present. Interceptor spec
files (3, unchanged) are carried over as-is.

**Exercised against the live API** (`ng serve` + real `dotnet run`, driven
with headless Chromium — no GUI available in this environment):

1. List loads 5 real items from `GET /api/quotes?page=1&size=5`.
2. Clicking "+ Add a quote" while signed out redirects to
   `/login?returnUrl=%2Fquotes%2Fnew` — the real guard, not just its unit test.
3. Signing in with the real seeded user (`test@example.com`/`password123`)
   lands back on `/quotes/new` (the return URL survived the round trip).
4. Submitting a real quote (`author: "Playwright Bot <n>"`) fires a real
   `POST /api/quotes`, shows `"Quote #8 by Playwright Bot <n> was added."`,
   and the form resets.
5. Navigating back to the list (in-app, not a hard reload — a hard reload
   would lose the in-memory token) shows `totalCount` went from 6 to 7. The
   new quote (id 8) did **not** appear on page 1 — confirmed by curling
   `GET /api/quotes?page=2&size=5` directly, which shows it on page 2. This
   is correct pagination behaviour (ids are ascending, the list refresh
   reloads the *current* page, not "page containing the newest item"), not a
   bug — flagging it here so it doesn't get mistaken for one.

## A mistake caught while reviewing my own diff

The ported `create-quote.spec.ts` test for the 401 case asserted
`.toContain('signed in')`, copied from Day 14's original test almost
verbatim. It failed on the first real run:

```
AssertionError: expected 'You need to sign in to do that.' to contain 'signed in'
```

Day 14's own bespoke error mapping produced different wording than this
app's shared `errorMappingInterceptor` (`genericMessageForStatus(401)` in
`core/interceptors/error-mapping-interceptor.ts` says "sign in", not "signed
in"). The fix is in the test, not the app — `errorMappingInterceptor`'s
message is the one already used consistently everywhere else in this app
(Day 15, Day 16 Task 1's login form, etc.), so changing the app to match a
copy-pasted test assertion would have been the wrong direction. Caught by
actually running the test against the real interceptor chain rather than
trusting that a ported assertion would still hold.

A second, non-test issue caught in the same pass: `auth-api.ts` had been
deleted in the previous iteration of this task (it was genuinely dead code —
nothing called `AuthApi.login()` when this was a single unrouted page). Now
that a login flow is back, `auth-api.ts` was re-added rather than resurrected
by mistake — worth noting only because it's a case of the *same file* being
correctly dead in one version of this task and correctly needed in the next;
neither state was a leftover oversight.

## When this would actually need signal-store/NgRx (my judgment call)

Unchanged reasoning from before, still holds after adding create: this
stays a plain-signals service as long as (1) one service owns state that
only this feature area reads, (2) transitions are simple `.set()` calls
without selectors/entity-normalization, and (3) nothing outside this feature
needs to react to its changes. I'd reach for `@ngrx/signals` when any of the
following starts being true:

- **Cross-feature sharing with independent write access** — e.g. a
  "recently viewed quotes" widget elsewhere in the app needing to both read
  and write the same quotes state. One writer (this feature), many readers
  is fine as a plain service; multiple independent writers is where the
  devtools/tracing story starts mattering.
- **Entity normalization** — if quotes started being referenced from
  multiple places (e.g. collections) such that an edit in one place must be
  visible everywhere, hand-writing that normalization is exactly what
  `signalStore`'s entity helpers exist to remove.
- **Interdependent derived signals growing past a handful** — there are 3
  `computed()`s today, each depending on 1-2 signals; past 8-10 cascading
  ones, `signalStore`'s `withComputed`/`withMethods` composition starts
  paying for itself over a hand-rolled class.
- **Optimistic updates with rollback, or undo/redo.** Creating a quote today
  is a plain "submit, wait, then refresh" - it does not update the UI
  optimistically. The moment that changes (show the new quote immediately,
  roll back if the POST fails), that's exactly the state-machine logic
  `signalStore`'s update patterns are built to make safe, and hand-rolling it
  is where subtle bugs creep in.

Creating a quote *did* add a real mutation (previously everything was
`GET`), which is the closest this task has come to one of these triggers —
but a plain `tap()`-triggered refresh after a successful POST is still just
"reload the data," not optimistic UI or rollback, so it doesn't cross the
line yet. If a future task asked for the new quote to appear in the list
*instantly*, before the refresh round-trip completes, that's the point where
I'd stop hand-rolling it.

## What would break if the real API changed

Unchanged from before for the list/detail endpoints (field renames degrade
silently to `undefined`/`NaN` rendering; a 404 body-shape change is absorbed
fine by `errorMappingInterceptor`; an endpoint-path change would make
`selectQuote()` land in `'notfound'` for every id, indistinguishable from a
real absence). Two new ones from the create-quote merge:

- **`POST /api/quotes` dropping `.RequireAuthorization()`**: the guard on
  `quotes/new` would become a client-side-only rule with nothing backing it
  server-side — same situation Task 1's detail guard is already in, just
  arrived at by the API changing instead of by initial design. Nothing would
  break; the guard would just stop mirroring a real constraint.
- **`ValidationProblemDetails.errors` keys changing case or wording**
  (e.g. the API started returning `"author"` lowercase instead of
  `"Author"`): `fieldMessage()`'s case-insensitive match means a *case*
  change wouldn't break anything, but a *field-name* change (e.g. `Author` →
  `Name`) would silently stop highlighting the right control - the
  server-level rejection message would still show as `serverMessage`, just
  without the specific field getting `aria-invalid`.
