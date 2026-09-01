# Day 14 Task 2 — Signal Forms preview

The same create-a-quote form as [Task 1](../task%20-%201/README.md), rebuilt against
`@angular/forms/signals` (the Angular 21.2 preview API) instead of Reactive Forms, against
the same real Week-1 endpoint: `POST /api/quotes` in `day - 2/QuotesApi`, taking
`{ author, text }` with `author` required 1–100 chars, `text` required 1–1000 chars, and
requiring a Bearer JWT.

This was **not** a port of Task 1's code. The API surface was read from the installed
package's actual type definitions and compiled source before writing anything (see
`node_modules/@angular/forms/types/signals.d.ts` and
`node_modules/@angular/forms/fesm2022/signals.mjs` in either task's `node_modules` — both
resolve to the same installed `@angular/forms@21.2.21`), and the component was restructured
around what that API actually gives you rather than replicating Task 1's structure.

## What the real API surface looks like (verified from source, not guessed)

- `form(modelSignal, schemaFn, options?)` returns a `FieldTree` — calling `quoteForm.author()`
  gives you a `FieldState` with signals: `value`, `errors`, `touched`, `dirty`, `invalid`,
  `submitting`, plus `markAsTouched()`, `reset()`, `focusBoundControl()`.
- The directive that binds a field to a native control is **`[formField]`**, not `[field]`
  (some older Signal Forms writeups from earlier previews use `[field]`; this version's
  compiled selector, confirmed in `signals.mjs`, is `"[formField]"`).
- `<form [formRoot]="quoteForm">` sets `novalidate` and wires the native `submit` event
  itself — there is no `(ngSubmit)` anywhere in this app.
- `submit()` (called internally by `[formRoot]`, or importable directly) runs
  `markAllAsTouched()` on every field before checking validity — confirmed by reading the
  compiled `submit()` function directly.
- `required()`, `maxLength()`, etc. also auto-bind the matching **native DOM property**
  (`required`, `maxLength`) on plain `<input>`/`<textarea>` elements — confirmed in
  `FormField.elementAcceptsNativeProperty()`.
- Submission errors returned from a `submission.action` callback can target a specific field
  via `{ fieldTree: quoteForm.author, kind: 'server', message: '...' }`; an error with no
  `fieldTree` attaches to the field passed to `submit()` (the root, in this app).
- A field's submission-set errors auto-clear the moment *that field's own value* changes
  again (`submissionErrors` is a `linkedSignal` keyed on the field's value) — confirmed in
  source and in `create-quote.spec.ts`.

## Comparison to the Task 1 (Reactive Forms) version

### 1. What became simpler

- **No manual `viewChild`/`ElementRef` for focus management.** Task 1 needed
  `viewChild<ElementRef>('authorInput')` plus a `.nativeElement.focus()` call. Here,
  `quoteForm.author().focusBoundControl()` does it — the field already knows which DOM
  element it's bound to.
- **No `markAllAsTouched()` call anywhere in this component.** `submit()` does it
  automatically before running validation.
- **No `(ngSubmit)` / `onSubmit()` method at all.** `[formRoot]` wires the native submit
  event declaratively; the whole request lifecycle lives in the `submission` config passed
  to `form()`.
- **No manual `[maxlength]` binding.** `maxLength(p.author, 100)` in the schema
  auto-sets the native `maxLength` DOM property — one declaration instead of a validator
  *and* a template attribute.
- **Fewer state signals.** Task 1 had an explicit `SubmitStatus` enum
  (`idle | submitting | success | error`). Here, `quoteForm().submitting()` is free, and
  "success" is just `createdQuote() !== null` — no status enum needed at all.
- **Error messages are declared once, at the validator.** Every `required`/`maxLength`/
  `validate` call takes a `message`, so the template/component just reads
  `errors()[0]?.message` — no kind-based `switch` statement like Task 1's `errorMessageFor`.

### 2. What's still rough (this is a preview API)

- **ARIA wiring is 100% manual, same as Reactive Forms.** I expected `[formField]` might
  auto-manage `aria-invalid`/`aria-describedby` the way it auto-manages `required`/
  `maxLength`. It does not — confirmed by grepping the compiled bundle for `aria-` and
  finding nothing. `[attr.aria-invalid]` and `[attr.aria-describedby]` in
  `create-quote.html` are exactly as much hand-wiring as Task 1 needed.
- **Testing an async submission is awkward.** `[formRoot]`'s submit listener is
  `onSubmit(event): void` — it calls `submit()` but never exposes or awaits the returned
  promise. In tests, dispatching a synthetic `submit` event and awaiting
  `fixture.whenStable()` was **not sufficient** to wait for the mocked HTTP round trip to
  finish; every async test needed an extra `setTimeout(resolve, 0)` macrotask flush after
  `httpMock.flush(...)`. This is documented and reused as `flushMicrotasks()` in
  `create-quote.spec.ts`. This isn't a documented gotcha anywhere I could find in the
  package — it came from the tests actually failing.
- **Everything is `@experimental`.** Every exported symbol from `@angular/forms/signals`
  carries an `@experimental` JSDoc tag with a version number (21.0.0 or 21.2.0). The API
  can change before it's stable.
- **The docs examples (JSDoc in the `.d.ts` files) are the most reliable source right now** —
  more reliable than blog posts or older preview writeups, since the directive name
  changed at least once (see `[field]` vs `[formField]` above).

### 3. Are the validators actually firing?

Yes — verified with real assertions, not just "it compiled": `create-quote.spec.ts` checks
`errors().map(e => e.kind)` directly against the field state after setting values, checks
the native `required`/`maxLength` DOM properties are actually set, and checks `aria-invalid`
flips in the real rendered DOM. All 12 tests pass (`ng test` output below).

### 4. Does the form submit the exact same `{ author, text }` payload?

Yes, byte-for-byte. `create-quote-api.ts` in this task posts the same
`CreateQuoteRequest { author: string; text: string }` shape to `/api/quotes` as Task 1.
Verified two ways:
- `create-quote.spec.ts`'s `expect(req.request.body).toEqual({ author: ..., text: ... })`.
- A real run against the live API (see Verification below): the exact same proxy setup
  (`proxy.conf.json` forwarding `/api` → `http://localhost:5225`) returned a real
  `201 Created` with `{"id":1,"author":"Grace Hopper","text":"...","isDeleted":false}`.

### 5. Does error handling still work?

Yes, and it's arguably more precise: server-side field errors are targeted with
`fieldTree: quoteForm.author` instead of the reactive-forms approach of calling
`control.setErrors({...control.errors, server: message})` on whichever control the
component decides to touch. The tradeoff is that the HTTP-error-mapping code now needs a
reference to the `FieldTree` to target errors at, which is a bit more coupling between the
"what went wrong" classification and "where does it show up" than Task 1's version, where
those were fully separate steps.

## Mistake caught and fixed

**`required()` does not trim whitespace** — same gap as Task 1, but I had to re-verify it
here since it's a different implementation of `required()` (Signal Forms' own, not
`Validators.required`). Reading the compiled source
(`node_modules/@angular/forms/fesm2022/signals.mjs`, the `isEmpty()` helper `required()`
calls) confirmed it only checks `value === ''`, `false`, or `null`/`undefined` — a
whitespace-only string like `"   "` satisfies `required()` and reports no error. Since the
real API's `[Required]` attribute *does* trim server-side (verified against the running API
in Task 1), a client that only used `required()` would let whitespace-only quotes pass
client-side validation and then get rejected by the server with a confusing round trip. Fixed
by adding a separate `validate()` rule (the same fix Task 1 needed, just against a different
validator implementation) — see the `isBlank()` check in `create-quote.ts` and the
dedicated "WRONG ASSUMPTION CHECK" test in `create-quote.spec.ts` that pins down that
`required()`'s own error is *not* what catches this case.

A second, smaller thing caught while writing tests (not a form-library bug, but a real
wrong assumption of mine): calling `field().value.set(...)` directly does **not** mark a
field dirty — only a real DOM `input` event does (confirmed in `nativeControlCreate` in the
compiled bundle). My first draft of the dirty/touched test asserted `dirty()` right after a
raw `.value.set()` and failed; the fix was to drive the actual `<input>` element with real
`input`/`blur` events instead, which also happens to be the more honest test since it's what
Task 1's Reactive Forms behave the same way (`setValue()` doesn't mark dirty there either).

## Verification

States tested — same list as Task 1, all in `create-quote.spec.ts` (12 tests) plus one real
run against the live API:

| State | How it was checked | Result |
|---|---|---|
| Pristine form | fresh render, no errors shown, native `required`/`maxLength` attrs set | pass |
| Dirty/touched | real `input`/`blur` DOM events on `#author` | pass |
| Blank/whitespace author or text | `validate()` rule fires; `required()` alone does *not* (explicit check) | pass |
| Author > 100 chars | `maxLength()` error + message | pass |
| Text > 1000 chars | dedicated test, same `maxLength()` mechanism as author | pass |
| Submit with invalid fields | focus moves to `#author` via `focusBoundControl()`, `aria-invalid`/`aria-describedby` wired, no HTTP request issued | pass |
| Submitting | `quoteForm().submitting()` drives `aria-busy`/button label | exercised implicitly in every submit test |
| Successful 201 | real HTTP mock returns `201`, success message renders, form resets | pass, **and** confirmed against the real running API (disposable SQLite DB + real JWT login, exact same proxy setup as Task 1) — real response: `{"id":1,"author":"Grace Hopper","text":"...","isDeleted":false}` |
| 400 field validation error | server error targets `quoteForm.author` via `fieldTree`, `#text` stays untouched | pass |
| Server error auto-clears | editing `#author` again after a targeted server error clears just that error | pass |
| 401 unauthorized | real 401 through the real proxy (unauthenticated), and mocked 401 → root-level message | pass |
| Network/server error | mocked connection failure → distinct message from a 400 | pass |

```
ng build   → Application bundle generation complete. [~14s], no errors
ng test    → Test Files  1 passed (1) / Tests  12 passed (12)
```

Nothing outside `day - 14/task - 2` was modified. The real-API verification used a
disposable SQLite database in a temp directory (seeded with a throwaway user, deleted
afterward) — `day - 2/QuotesApi`'s own database and Task 1's implementation were not
touched.
