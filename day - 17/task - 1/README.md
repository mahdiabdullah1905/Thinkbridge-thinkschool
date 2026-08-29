# Day 17 — Task 1: Deploy to Azure Static Web Apps

The Angular app (`day - 16/task - 2`) is deployed to Azure Static Web Apps and calls the real
Week-1 API (`day - 2/QuotesApi`, deployed as the `quotes-api` Container App since Day 5) with
**no client secret stored anywhere** — not in the frontend, not in this repo, not in app
settings.

The brief asked for this via "Managed Identity." The investigation below explains why that
literal mechanism cannot run in a browser, and what the correct Azure-native equivalent is
instead.

## The two trust boundaries — do not conflate them

This deployment relies on **two separate, unrelated** authentication mechanisms. Confusing them
is the exact mistake this README exists to prevent.

### 1. Secretless platform trust: Static Web Apps → Container App

This answers *"is this request allowed to reach the API at all?"* — it knows nothing about which
human is using the app.

- Azure Static Web Apps has a first-class feature for exactly this shape:
  [linking an existing Container App as a backend](https://learn.microsoft.com/en-us/azure/static-web-apps/apis-container-apps)
  (`az staticwebapp backends link`, requires the **Standard** SWA plan).
- Once linked, requests to `https://<swa-host>/api/*` are proxied **server-side, at Azure's own
  edge** to the Container App's `/api/*`. The browser never talks to the Container App's own
  public hostname at all — every request the SPA makes is same-origin against the SWA.
- Linking automatically added an identity provider named `azureStaticWebApps` to the Container
  App's built-in (platform-level) authentication, confirmed via `az containerapp auth show`:
  ```json
  {
    "globalValidation": { "unauthenticatedClientAction": "RedirectToLoginPage" },
    "identityProviders": {
      "azureStaticWebApps": {
        "enabled": true,
        "registration": { "clientId": "gentle-river-0e0339700.7.azurestaticapps.net" }
      }
    },
    "platform": { "enabled": true }
  }
  ```
- **What this is not**: it is not a bearer token the browser holds, not an Entra ID access
  token issued to end-user code, and **not a Managed Identity credential used by the browser** —
  a browser has no IMDS endpoint to talk to and cannot hold a managed identity. Microsoft's own
  documentation does not spell out the exact wire protocol between the SWA edge and the linked
  backend, and we are not going to assert more than the docs say. What we can state as directly
  observed fact:
  - No secret, key, or credential for this trust relationship exists anywhere in this repository,
    in the SPA's built JavaScript, or in the Container App's configured secrets.
  - The trust is provisioned and enforced entirely by the Azure platform when the two resources
    are linked, and is revoked the moment they're unlinked.
- **Effect confirmed empirically**: after linking, calling the Container App's own public FQDN
  directly and anonymously now returns `401 Unauthorized` / `WWW-Authenticate: Bearer` on every
  route, including `/health` — see Verification below. Only traffic proxied through the linked
  SWA gets through.

### 2. User authentication: the existing HS256 JWT (completely unchanged)

This answers *"which signed-in user is making this request, and are they allowed to POST a
quote?"* — it is layered on top of trust boundary #1 and was not touched by this task.

- `POST /api/auth/login` still verifies a bcrypt password hash and issues a self-signed HS256 JWT
  (`day - 2/QuotesApi/Extensions/ProgramExtensions.cs`, `GenerateJwt`), same as every previous day.
- `POST /api/quotes` still carries `.RequireAuthorization()` and still 401s an anonymous request
  before it reaches the handler.
- The Angular `authInterceptor` still attaches `Authorization: Bearer <token>` the same way it
  always has.
- **This is genuinely not Managed Identity or Entra ID**, and nothing in this deployment claims
  otherwise. It is exactly the JWT flow documented in `day - 16/task - 2/README.md`.

Trust boundary #1 makes the API unreachable by anyone except traffic from this specific SWA.
Trust boundary #2 then decides, among requests that made it through #1, who's allowed to do what.
Neither one is a substitute for the other.

## Why not literal Managed Identity

A prior investigation (see conversation history / PR discussion) ruled out forcing the term
"Managed Identity" onto this deployment:

- Managed Identity is a credential for **Azure compute** (a Function, an App Service, a Container
  App, a VM) to request Entra ID tokens with no stored secret. Code running in a visitor's browser
  is not Azure compute and has no path to an IMDS endpoint — it cannot hold or use a managed
  identity, full stop.
- The only way to get a literal Managed Identity into this picture would be to introduce a new
  backend-for-frontend (a separately deployed Function App or Container App) with its own system
  identity, plus a new Entra ID app registration for `quotes-api` (Application ID URI + app role),
  granted to that identity. This requires the same Standard-tier SWA cost as the approach we took,
  plus an extra piece of compute, an app registration, and an admin-consent step — for an outcome
  the built-in linked-backend feature already gives us automatically. We did not build this.
- Microsoft's own docs confirm SWA's Free-tier co-located "Managed Functions" API **cannot** use a
  managed identity at all — only a separately linked ("bring your own") backend can, and linking
  any backend type requires the Standard plan regardless.

## Azure resources

| Resource | Name | Change this task made |
|---|---|---|
| Static Web App | `swa-quotes-day17` (resource group `thinkschool-rg`, East Asia) | Upgraded **Free → Standard** (`$9/mo`, prorated daily) |
| Container App | `quotes-api` (same resource group, Central India, existing since Day 5) | Linked as backend; new revision `quotes-api--day17linked` deployed |
| Backend link | `azureStaticWebApps` identity provider | Auto-created by `az staticwebapp backends link` |

No new App Registrations, no new Managed Identities, no new secrets were created for this task.
The Container App's pre-existing system-assigned identity (used only for `AcrPull` since Day 5)
was not touched.

## What changed in this repo, and what was reverted

An earlier iteration of this task (before the architecture investigation) worked around the
cross-origin problem with a narrow CORS allow-list on `quotes-api` and an absolute API base URL
baked into the Angular production build. Once the linked backend made the browser's calls
same-origin, **both workarounds became unnecessary and were fully reverted**:

- `day - 2/QuotesApi/{Program.cs,Extensions/ProgramExtensions.cs,appsettings.json}` — CORS policy
  removed; these files are now byte-identical to their pre-Day-17 committed state (confirmed via
  `git diff`, zero output).
- `day - 16/task - 2/src/app/core/{quotes-api.ts,auth-api.ts}` — back to relative `/api/...` calls;
  byte-identical to their pre-Day-17 state.
- `day - 16/task - 2/src/environments/` — removed entirely (no longer needed).
- `day - 16/task - 2/angular.json` — `fileReplacements` entry removed.

What's actually left in the diff for this task:

- `day - 16/task - 2/public/staticwebapp.config.json` — SPA fallback routing + baseline security
  headers (needed regardless of backend approach).
- `day - 16/task - 2/src/index.html` — added a `<meta name="description">` (Lighthouse SEO fix).
- `.github/workflows/deploy-day17-swa.yml` — CI workflow for future deploys via
  `Azure/static-web-apps-deploy@v1` (references a GitHub secret you still need to add yourself —
  see below; not committed with any token in it).

## Verification performed

**Functional chain, entirely through the SWA's own relative `/api/*` paths** (no cross-origin call
occurs anywhere in this flow):

```
GET  /api/quotes                                  -> 200 {"page":1,...,"items":[]}
GET  /api/quotes/1                                -> 404 (contract unchanged)
POST /api/auth/login  {test@example.com/...}      -> 200, real HS256 JWT issued
POST /api/quotes      (no Authorization header)   -> 401
POST /api/quotes      (Authorization: Bearer ...) -> 201, quote created
GET  /api/quotes                                  -> 200, totalCount now reflects the new quote
```

**Direct-access lockout**, confirmed with `curl` straight against the Container App's own public
hostname (bypassing the SWA entirely):

```
GET https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/api/quotes
-> 401 Unauthorized, WWW-Authenticate: Bearer realm="quotes-api...."

GET https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/health
-> 401 Unauthorized (same lockout — every route, not just /api)
```

**Real browser session** (headless Chromium via Playwright, since there's no GUI in this
environment), fresh cookie/storage state, driven against the live deployed URL:

- Visiting `/quotes/new` while signed out redirected to `/login?returnUrl=%2Fquotes%2Fnew` — the
  real route guard, not a mock.
- Every API request captured from the network layer was same-origin
  (`https://gentle-river-0e0339700.7.azurestaticapps.net/api/...`), each carrying Azure's own
  `x-ms-middleware-request-id` response header — proof the request actually transited the SWA
  proxy rather than hitting a cached/mocked response.
- After signing in with the seeded test user, the subsequent `GET /api/quotes` carried a real
  `Authorization: Bearer <redacted>` header — the JWT interceptor is unaffected by the backend
  change.

**Identity provider existence**, confirmed via `az containerapp auth show -n quotes-api` (see
JSON block above) — the `azureStaticWebApps` provider is present and enabled.

**Custom domain**: `az staticwebapp hostname list` returns `[]`. No custom domain is configured.
Same as last time — we don't have one available, and nothing has been invented here.

## Lighthouse (`/quotes`, real deployed URL, two runs after this change)

| Category | Run 1 | Run 2 |
|---|---|---|
| Accessibility | 100 | 100 |
| Best Practices | 100 | 100 |
| SEO | 100 | 100 |
| **Performance** | **89** | **79** |

Accessibility, Best Practices, and SEO all meet the ≥95 bar. **Performance does not**, and I'm not
hiding that. It did improve over the CORS-based deployment's 59–85 range (removing the CORS
preflight round-trip helps), but it's still bounded by the same real cross-region network path —
East Asia (SWA edge) to Central India (Container App) — for the initial data fetch that blocks
LCP, on a subscription whose region-restriction policy doesn't offer a same-region pairing for
both services. Fixing this further would mean an architectural change (SSR/prerendering, or moving
one of the two resources to a shared region if the policy allowed it) outside this task's scope.

## Rollback

The pre-existing (non-CORS, non-linked) API image is still in ACR as `quotes-api:0.1.0`, and the
Container App is in multi-revision mode with the prior CORS-based revision
(`quotes-api--day17cors`) still `Active` at 0% traffic (not deleted). If this needs to be undone:

```
az staticwebapp backends link --help    # to find the unlink equivalent, or:
# Azure portal: Static Web App -> APIs -> select the container app -> Unlink
az staticwebapp update -n swa-quotes-day17 -g thinkschool-rg --sku Free   # revert SKU (after unlinking)
```

**A correction to something I said in the previous round**: I previously described deactivating a
Container App revision as "instantly reactivatable, effectively free rollback insurance." That
turned out to be wrong in practice — the revision I deactivated in the prior session
(`quotes-api--0000007`) is no longer listed at all, even with `--all`, and
`az containerapp revision show` returns `RevisionNotFound` for it. Whether that's revision
retention/garbage collection or something else, I don't fully know — but the practical lesson is:
**deactivating a revision is not a guaranteed durable rollback point.** For this round, I left
`quotes-api--day17cors` merely at 0% traffic (not deactivated), and I'm flagging this explicitly
rather than repeating the same overclaim.

## A second mistake caught this round

The first `az staticwebapp backends link` call failed with `Operation returned an invalid status
'Not Found'`. `--debug` showed why: Git Bash's automatic path conversion had rewritten the
leading `/subscriptions/...` resource ID into a mangled Windows path
(`C:/Program Files/Git/subscriptions/...`) before it ever reached the Azure REST call. Fixed by
setting `MSYS_NO_PATHCONV=1` for that command. Not a logic bug, but worth recording since it would
silently mangle any future Azure CLI command that takes a full ARM resource ID as an argument from
this shell.

## What would break if this configuration changed

- **Unlinking the backend** (or deleting/recreating the SWA) removes the `azureStaticWebApps`
  identity provider from the Container App, which currently has `unauthenticatedClientAction:
  RedirectToLoginPage` as its only configured provider — the Container App would then reject
  *all* traffic, including the SWA's own, until either the link is restored or authentication is
  removed from the Container App entirely (`az containerapp auth show` → remove the provider).
- **Downgrading the SWA SKU back to Free** requires unlinking the backend first (Standard is a
  prerequisite for any linked backend, not just Container Apps).
- **Changing `Jwt:Key`/`Issuer`/`Audience`** still invalidates outstanding tokens immediately, same
  as before — that risk is entirely inside trust boundary #2 and is unaffected by anything in this
  task.
- The Container App still has **no persistent volume** — a fresh revision (like
  `quotes-api--day17linked`) always starts with an empty, freshly-migrated SQLite database
  (seeded users only). This predates this task and is unrelated to the linked-backend change.

## Final git status

```
 M day - 16/task - 2/src/index.html
?? .github/workflows/deploy-day17-swa.yml
?? day - 16/task - 2/public/staticwebapp.config.json
?? day - 17/task - 1/README.md
```

Nothing has been committed or pushed. `day - 2/QuotesApi` and `day - 16/task - 2`'s core files
have zero diff against their last committed state — the only real changes are the two small,
genuinely-needed frontend/config files above, this README, and the CI workflow.
