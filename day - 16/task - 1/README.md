# Day 16, Task 1 — Routing, lazy loading, guards

Standalone, zoneless Angular 21 app with client-side routing over the real
Week-1 API (`day - 2/QuotesApi`, `dotnet run`, `http://localhost:5225`). Built
by copying the already-working `QuotesApi`/`AuthApi`/`AuthTokenStore` +
interceptor chain from Day 15 rather than re-deriving auth/HTTP handling from
scratch, and adding routing, a functional guard, lazy loading, and a View
Transition on top.

## The real API contract (confirmed with curl before writing any routing code)

```
curl "http://localhost:5225/api/quotes?page=1&size=3"
{"page":1,"size":3,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}

curl "http://localhost:5225/api/quotes/1"
{"id":1,"author":"Authorized","text":"With token","isDeleted":false}

curl "http://localhost:5225/api/quotes/999999"
(empty body, HTTP 404)

curl -X POST "http://localhost:5225/api/auth/login" -d '{"email":"test@example.com","password":"password123"}'
{"accessToken":"...","refreshToken":"...","expiresIn":900}
```

`test@example.com` / `password123` is the one seeded user
(`day - 2/QuotesApi/Data/AppDbContext.cs`), already used for auth testing in
Day 15's README — not invented for this task.

**Read first, before assuming the guard mirrors the server:** `GET
/api/quotes` and `GET /api/quotes/{id}` have no `.RequireAuthorization()` in
`ProgramExtensions.MapQuoteEndpoints` — only `POST`/`DELETE` do. The API does
not require a token to read quotes at all. So the auth guard added here is an
**app-level routing rule for this exercise**, not a client mirror of a
server-side 401: viewing quote **detail** is gated behind sign-in (a real,
testable redirect), while the **list** stays public. This is a deliberate
choice, documented in `src/app/auth/auth.guard.ts` — flagging it here in case
a different route split was intended.

## Files

```
src/app/
  quotes-api.ts                  - getQuotes, getQuoteById (typed, matches the contract above)
  auth-api.ts, auth-token-store.ts, errors/app-error.ts,
  interceptors/*.ts              - copied unmodified from day - 15/task - 1 (auth/error-mapping/retry)
  auth/auth.guard.ts (+ .spec)   - CanActivateFn: redirects to /login?returnUrl=... when no token
  quote-list/                    - eager (bundled in main.js): GET /api/quotes, paginated
  quote-detail/                  - lazy-loaded (loadComponent), route param bound via input()
  login/                         - lazy-loaded, real POST /api/auth/login
  app.routes.ts                  - route table (list eager, detail + login lazy, detail guarded)
  app.config.ts                  - provideRouter(routes, withComponentInputBinding(), withViewTransitions())
  app.ts / app.html / app.css    - shell: header nav + <router-outlet/>
```

`getQuoteById` didn't exist in Day 15's copy of `quotes-api.ts` (it only had
`getQuotes`/`createQuote` for the HttpClient exercise) — added it here, typed
against the curl output above. Also dropped the unused `createQuote`/
`CreateQuoteRequest` that came along with the copy: this task has no
create-quote feature, so carrying them over would have been dead code.

## Design/DI

Standalone components, `provideZonelessChangeDetection()`, `inject()`
everywhere (no constructor injection). `QuoteDetail.id` is
`input.required<string>()`, bound to the `:id` route param by
`withComponentInputBinding()` — no manual `ActivatedRoute.paramMap`
subscription. Both list and detail effects wrap their `.subscribe()` call in
`untracked()`, for the same reason documented in Day 15: `authInterceptor`
reads `AuthTokenStore`'s token signal synchronously during `.subscribe()`,
and without `untracked()` that read gets attributed to the effect as a
dependency, causing an extra unwanted refetch on every sign-in/out.

## Verification

No GUI is available in this environment, so "check the Network tab" was done
by driving a real headless Chromium (Playwright) against `ng serve` (proxying
to the live `dotnet run` API) and reading actual network/router events — not
just inspecting the code.

```
npx ng build   → initial bundle: main-*.js (6.69 kB) + 2 tiny framework chunks — no quote-detail, no login
                 lazy chunks:    chunk-*.js  "login" (29.58 kB), chunk-*.js  "quote-detail" (2.31 kB)
npx ng test    → 4 test files, 14 passed (3 interceptor spec files copied from Day 15, unmodified + new auth.guard.spec.ts)
```

Against the running app (`ng serve` + real `dotnet run` API, fresh browser
context per scenario):

1. **List loads from the real API.** `GET /api/quotes?page=1&size=5` observed
   on the wire; 5 real items rendered (`Authorized`, `Test`, `Mahatma Gandhi`,
   ...).
2. **Click → detail, correct id.** Clicked the *second* list item (`Test`,
   id 3): navigated to `/quotes/3`, fired `GET /api/quotes/3`, and the
   detail page's author matched the list row's author exactly.
3. **Lazy loading, confirmed at runtime, not just from the build report.**
   Loading `/quotes` alone requests only `main.js` + 2 framework chunks — no
   `quote-detail` chunk on the wire. The `quote-detail` chunk request first
   appears at the exact moment the login→detail redirect completes (i.e. the
   first time the guarded route is actually entered), never before.
4. **Unauthenticated user is redirected, both ways.** Clicking a quote link
   while signed out redirects to `/login?returnUrl=%2Fquotes%2F1` instead of
   loading the detail component. A **direct/hard navigation** straight to
   `/quotes/1` (simulating a bookmark or refresh, not just a blocked link)
   redirects the same way.
5. **Authenticated path works end to end.** Signed in as
   `test@example.com`/`password123` (the real seeded user) from a redirect
   with `returnUrl=/quotes/1`; landed back on `/quotes/1` with the real quote
   text (`"With token"`) rendered from a live `GET /api/quotes/1`.
6. **View Transition is actually invoked, not just configured.** Patched
   `document.startViewTransition` before a list→detail navigation and
   confirmed it was called exactly once for that single navigation (the
   browser reports `startViewTransition` as supported and Angular's router
   calls it).
7. **Real 404 handled sensibly.** Client-side navigation to `/quotes/999999`
   while authenticated: `GET /api/quotes/999999` came back `404` with an
   empty body (confirmed with curl too), and the detail page renders "Quote
   999999 does not exist." — not a blank screen, not a raw error dump.
8. **Sign-out re-engages the guard.** After sign-out, revisiting `/quotes/1`
   in the same session redirects to `/login` again — the guard reacts to the
   token being cleared, not just to a fresh page load.

Screenshots for each of the above are in the verification run's `shots/`
directory (not committed — this repo doesn't track screenshot artifacts).

## Something that looked like a bug and wasn't

The first run of scenario 4 (direct navigation to `/quotes/1` while signed
out) showed the browser still on `/quotes/1` with a **blank page** — header
rendered, `<router-outlet>` empty, no redirect. That looked exactly like a
broken guard. Reproducing it with console/network logging showed the actual
sequence: Playwright's `networkidle` resolved *before* Angular finished
zoneless bootstrap and the async `login` chunk import that the guard's
redirect depends on — the redirect was still in flight, not missing. Waiting
for the actual resulting URL (`page.waitForURL(/\/login/)`) instead of
trusting "network idle" showed the redirect completing correctly on every
run. Recorded here because it's the kind of thing that's easy to misreport as
"the guard doesn't work" if you stop at the first screenshot.

## What would break

- **A non-numeric `:id`** (e.g. `/quotes/abc`): `Number('abc')` is `NaN`, so
  the app would request `GET /api/quotes/NaN`. Not exercised here — the task
  asked for a non-existent *id* (`999999`), which is what's covered above.
- **The guard's route split is a judgment call**, not a spec: if the intent
  was "the whole app requires sign-in" or "nothing does," that's a one-line
  change to `app.routes.ts` (move `canActivate` onto `quotes`, or remove it),
  not a redesign.
- **`AuthTokenStore` is in-memory only** (inherited from Day 15, unchanged):
  a hard refresh always signs the user out, by design — there's no
  persistence to lose track of, but it does mean the guard will redirect
  again after any full page reload, even mid-session.
