# Day 14 Task 1 — Reactive forms + accessibility

Standalone, zoneless Angular 21 form for `POST /api/quotes` against the real Week-1 API
(`day - 2/QuotesApi`). See the conversation history for the full write-up of the API
contract, component, and service. This section documents the accessibility verification.

## Verification

### What was actually run

- **App**: the real `create-quote` component, served by `ng serve` (dev-server proxy to
  the real `dotnet run` API), in a real Chromium browser — not a mock, not jsdom.
- **Driver**: Chrome DevTools Protocol (CDP), scripted from Node, dispatching genuine
  `Input.dispatchKeyEvent` keyboard events (Tab, Shift+Tab, Enter, Backspace) and real
  text input (`Input.insertText`) into the actual rendered page, then reading back the
  real DOM and the real computed accessibility tree via `Accessibility.getPartialAXTree`
  / `Accessibility.getFullAXTree`.

### Screen reader caveat — read this first

**Windows Narrator itself was not used**, and no claim is made that it was. There is no
audio capture, transcript API, or text log for Narrator's speech in this environment, so
there is no way to honestly report "what Narrator said." Enabling Narrator would produce
audio only, with nothing for me to inspect.

Instead, the check above queries Chrome's `Accessibility` domain directly — this is the
same computed accessibility tree (role, accessible name, `invalid` state, `description`
resolved from `aria-describedby`) that Chrome hands to Windows UI Automation, which is
exactly what Narrator and NVDA read from. It's a real, non-mocked verification of the
accessibility wiring, but it is tree-level, not an audio transcript. A live Narrator/NVDA
pass by a human is still worth doing before calling this fully done.

### Results

**Keyboard-only navigation** (real Tab/Shift+Tab, starting from `<body>`):

| Step | Focused element | Accessible name |
|---|---|---|
| Tab 1 | `input#author` | "Author" |
| Tab 2 | `textarea#text` | "Quote text" |
| Tab 3 | `button[type=submit]` | "Add quote" |
| Tab 4 | wraps to `<body>` | — |
| Shift+Tab (from body) | `button[type=submit]` | "Add quote" |

No stray tab stops, order matches visual/DOM order.

**Accessible names** (via `Accessibility.getPartialAXTree`, `nativeSource: "labelfor"`):
`#author` → `role: textbox`, `name: "Author"`; `#text` → `role: textbox`,
`name: "Quote text"`. Both resolved through the real `<label for>` relationship, not
`aria-label`.

**Submit the empty form via real Enter key** (button focused, `Input.dispatchKeyEvent`
with `type: keyDown/keyUp`, `text: "\r"`):
- Focus moved to `input#author` and stayed there (checked at +0/50/150/400/800ms — no
  reversion).
- `#author`: `aria-invalid="true"`, `aria-describedby="author-error"`,
  `#author-error` text: `"Enter the author's name."`
- `#text`: `aria-invalid="true"`, `aria-describedby="text-error"` also set, confirming
  `markAllAsTouched()` flags both fields, not just the one that gets focus.
- Independently confirmed via `Accessibility.getPartialAXTree` on `#author`:
  `properties.invalid = true`, `description = "Enter the author's name."` — i.e. Chrome's
  accessibility engine resolves the `aria-describedby` text into the node's description,
  which is what a screen reader announces alongside the field.

**Real typing into an invalid value** (typed `"   "` into `#text`, Tab away to blur/touch
it): `aria-invalid` flips to `"true"`, `aria-describedby="text-error"`,
`#text-error` text: `"Enter the quote text."` Then: Shift+Tab back in, real Backspace ×3,
typed a valid sentence, Tab away — `aria-invalid` back to `"false"`, `#text-error` element
removed from the DOM (not just visually hidden).

**Note on the "author too long" case**: typing 101 characters into `#author` is clamped
to 100 by the native `maxlength` attribute before it ever reaches the DOM — Chrome does
not let you type or paste past `maxlength` in a real browser. `Validators.maxLength(100)`
on the control is still correct and still fires (see the existing `create-quote.spec.ts`,
which sets the value programmatically past the DOM's typing limit), but real keyboard
typing alone cannot reach that error path — this is expected browser behavior, not a gap.

**Full-page accessible-node summary** (`Accessibility.getFullAXTree`, filtered to
textbox/button/heading/text nodes): one heading ("Add a quote"), two textboxes ("Author",
"Quote text"), one button ("Add quote"), and static text nodes for the subtitle and
current field values/errors — no unexpected or unlabeled interactive nodes.

### Cleanup

The debug Chrome instance (remote-debugging profile), `ng serve`, and all temp driver
scripts were closed/deleted after the run. Nothing outside `day - 14/task - 1` was
touched.
