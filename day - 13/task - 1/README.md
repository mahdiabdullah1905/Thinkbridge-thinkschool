# Day 13, Task 1: Signals + zoneless + standalone

## What was built

A small standalone Angular 21 app (`ng new --standalone --zoneless`, no
NgModule anywhere) with one screen: a paginated list of quotes read from
the real Week 1 API (`day - 2/QuotesApi`, `GET /api/quotes`). No mock
data anywhere — every quote on screen came from the actual SQLite-backed
API running on `http://localhost:5225`.

The dev server proxies `/api/*` to that API (`proxy.conf.json`) instead
of adding CORS headers to `QuotesApi`, so nothing in `day - 2` needed to
change for this task.

## Signals

All in `src/app/app.ts`:

- `signal()` — `page`, `pageSize`, `quotes`, `totalCount`, `loading`,
  `error`. These are the raw state; nothing else is allowed to hold a
  duplicate copy of them.
- `computed()` — `totalPages`, `canGoPrevious`, `canGoNext`, and
  `status` (`'loading' | 'error' | 'empty' | 'success'`). All four are
  pure derivations of the signals above — no separate state to keep in
  sync, no chance of `status` disagreeing with `loading`/`error`.
- `effect()` — one, in the constructor, and it does not derive a value.
  It calls the API whenever `page` or `pageSize` changes (including once
  immediately on startup) and writes the result into `quotes` /
  `totalCount` / `error`. That's a genuine side effect (an HTTP call),
  which is why it's an `effect()` and not a `computed()` — a `computed()`
  has to be a pure, synchronous function of other signals, and firing a
  network request isn't that.

## Control flow

All in `src/app/app.html`:

- `@switch (status())` picks which panel to show — loading text, the
  error message, the empty message, or the quote list. `status` already
  collapses three signals into one of four states, so a switch over it
  reads better than a chain of `@if`/`@else if`.
- `@for (quote of quotes(); track quote.id)` renders the list, tracked
  by the API's real `id` so Angular doesn't tear down and rebuild rows
  it doesn't need to.
- `@if (status() !== 'loading')` hides the pager while a request is in
  flight, so you can't mash Next mid-request.

## Dependency injection

`inject()` everywhere, no constructors:

- `src/app/quotes-api.ts`: `private readonly http = inject(HttpClient);`
- `src/app/app.ts`: `private readonly quotesApi = inject(QuotesApi);`

## Zoneless

`src/app/app.config.ts` calls `provideZonelessChangeDetection()`
explicitly. Turns out that's almost redundant in Angular 21 — I checked
`node_modules/@angular/core`'s public exports and `zone.js` isn't even a
dependency of a freshly-scaffolded app anymore, so a component with no
zone-based provider is already zoneless by default. I added the call
anyway so the intent is visible in the code instead of relying on a
default that could change.

What zoneless actually changes: with Zone.js, Angular used to patch
`setTimeout`, `Promise`, DOM event listeners, etc., so that after *any*
async callback finished, it would run change detection on the whole
component tree just in case something changed. That works, but it's a
blunt instrument — it can't tell what changed, so it checks everything.
Zoneless removes that patching entirely. Instead, writing to a `signal()`
is itself the notification: Angular knows exactly which components read
that signal (the ones that used it in a template or a `computed()`) and
only re-renders those. Nothing about this makes the app "faster" by
itself — a small app like this one would be imperceptibly different
either way — but it does mean the reactive graph is explicit in the
code instead of implicit in whatever Zone.js happened to patch.

## Verification

Real API, running from `day - 2/QuotesApi`:

```
dotnet build                                    # Build succeeded, 0 errors
dotnet run --urls http://localhost:5225

curl "http://localhost:5225/api/quotes?page=1&size=3"
{"page":1,"size":3,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}
```

Angular side:

```
npm test    # ng test (Vitest)
 Test Files  1 passed (1)
      Tests  3 passed (3)

npm run build    # ng build
Initial chunk files | Names | Raw size | Estimated transfer size
main-*.js        | main   | 137.90 kB |               40.33 kB
Application bundle generation complete.
```

`grep`-ing the production bundle for `Zone.js` / `ZoneAwarePromise`
comes back with 0 matches — there's no zone.js runtime patch shipped at
all, not just "not imported in app.config.ts".

State transitions were exercised against the real dev server
(`ng serve`, proxying to the real API) with a headless-Chromium script,
not by hand-waving:

- **Loading → data**: first paint shows "Loading quotes...", then the
  real first page of quotes (Authorized / Test / Mahatma Gandhi / AI
  Agent / Jaeger — whatever is actually in `quotes.db`).
- **Pagination**: clicking Next issues a real `GET /api/quotes?page=2`
  and renders whatever comes back (in this run, the "Task6
  Verification" quote left over from an earlier day's testing).
- **Empty state**: typing `9999` into "Go to page" is a legitimate
  request the API answers with `items: []` (page 9999 doesn't exist,
  but the request itself is valid) — no backend data was touched to
  fake this.
- **Error state**: stopped the real `dotnet run` process, reloaded the
  page, and the proxy's failed request produced a genuine
  `HttpErrorResponse`, rendering "Could not reach the quotes API. Is it
  running?". Restarted the API afterward and confirmed `GET
  /api/quotes` was back to 200.
- **Console**: no uncaught errors or exceptions in any of the above —
  only Vite's dev-server debug lines, Angular's dev-mode notice, and
  (during the error-state run) the browser's own "failed to load
  resource: 500" log for the one request that was expected to fail.

No `ng lint` — Angular 21's CLI doesn't ship a lint builder out of the
box and this workspace never had ESLint added, so there's nothing
configured to run.

## What would break

The effect ignores stale responses by checking `this.page() !==
requestedPage` before applying a result, but there's no actual request
cancellation — if you spam Next several times, every request still
fires and completes, they're just discarded if a newer page was
requested in the meantime. On a slow network this means unnecessary
load on the API for pages the user already navigated away from. A more
complete version would cancel the in-flight request (e.g. an
`AbortController` or an RxJS-based approach with `switchMap`) instead of
just ignoring its result.
