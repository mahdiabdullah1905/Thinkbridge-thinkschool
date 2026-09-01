# Day 17 — Task 1: Deploy to Azure Static Web Apps with Managed Identity

The Angular app (`day - 16/task - 2`) is deployed to Azure Static Web Apps and calls the real
Week-1 API (`day - 2/QuotesApi`, deployed as the `quotes-api` Container App since Day 5) with
**no client secret stored anywhere** — not in the frontend, not in this repo, not in app settings —
using a genuine **system-assigned Managed Identity** for the service-to-service hop.

## Final architecture

```
Browser (Angular SPA)
   │  same-origin, relative /api/* calls, existing HS256 JWT in Authorization
   ▼
Azure Static Web Apps  (swa-quotes-day17, Standard plan)
   │  linked-backend proxy - browser never sees anything past this point
   ▼
Azure Function App  (func-quotes-bff-day17, Flex Consumption, Central India)
   │  system-assigned Managed Identity acquires a real Entra ID token,
   │  no client secret anywhere - then forwards the request with:
   │    Authorization: Bearer <Managed-Identity-acquired Entra token>
   │    X-User-Token:  Bearer <the browser's original HS256 JWT, untouched>
   ▼
Azure Container App  (quotes-api, unchanged endpoints/business logic since Day 2)
   - Container Apps' built-in Entra ID auth validates the Entra token (platform-level)
   - the app's own JwtBearer scheme validates the HS256 JWT from X-User-Token,
     falling back to Authorization for every caller not behind this BFF
```

Two independent, non-overlapping trust layers exist here. Confusing them is the exact mistake
this README exists to prevent:

### Layer 1 — service trust: real Managed Identity, not the browser

Answers *"is this request genuinely coming from our trusted backend?"* — knows nothing about
which human is signed in.

- **The Function App (`func-quotes-bff-day17`) has a system-assigned Managed Identity**, confirmed
  directly: `az ad sp show --id 57929bc0-08c5-48f2-9fba-b5e05f30c7d3` returns
  `servicePrincipalType: ManagedIdentity`, `appId: 00661bb9-43dc-4778-8513-fb9c47239188`.
- **Token acquisition uses zero client secret.** The Function's code (`QuotesApiProxy.cs`) calls
  `DefaultAzureCredential.GetTokenAsync(new TokenRequestContext(["api://9ec5db29-.../​.default"]))`.
  On real Azure compute this resolves to `ManagedIdentityCredential` — there is no secret, no
  certificate, nothing stored to read. `az functionapp config appsettings list` on this app
  contains only `QuotesApi__BaseUrl` and `QuotesApi__AppIdUri` (both public, non-secret values) —
  no client ID/secret pair anywhere.
- **`quotes-api` genuinely validates that token** via Container Apps' built-in Microsoft Entra
  authentication (`az containerapp auth microsoft update`, configured with a client ID, issuer, and
  allowed audience — **no client secret parameter was ever set**). This is platform-level: no
  custom JWT-parsing code was added to `quotes-api` for this layer.
- **This was proven, not just asserted.** A direct call from the Function to `quotes-api`, using
  its real Managed-Identity-acquired token, returned an actual `200 OK` with real quote data — the
  API *accepted* the token, it didn't just get issued one. See Verification below.
- **The browser never has, and is never described as having, a Managed Identity.** A browser has
  no IMDS endpoint to talk to; it cannot request or hold this kind of token. The Function is the
  only thing in this whole system that uses Managed Identity.

### Layer 2 — user authentication: the existing HS256 JWT (still completely unchanged)

Answers *"which signed-in user is making this request, and are they allowed to POST a quote?"* —
layered independently on top of Layer 1.

- `POST /api/auth/login` still verifies a bcrypt password hash and issues the same self-signed
  HS256 JWT it always has (`day - 2/QuotesApi/Extensions/ProgramExtensions.cs`, `GenerateJwt`) —
  same secret key, same issuer/audience, same 15-minute expiry, untouched.
- `POST /api/quotes` still carries `.RequireAuthorization()` and still 401s a request with no valid
  user JWT before it ever reaches the handler.
- The Angular `authInterceptor` still attaches `Authorization: Bearer <token>` exactly as it always
  has — **the browser's own behavior did not change at all** for this task.
- The only new code is in `quotes-api`'s JWT scheme: one additive `OnMessageReceived` handler that
  checks for an `X-User-Token` header first (where the Function relays the browser's JWT once
  `Authorization` is needed for the Entra token instead), and falls back to reading `Authorization`
  normally when that header is absent. Every caller that isn't behind the Function — local dev,
  direct testing — is byte-for-byte unaffected; this was regression-tested locally in Docker before
  ever touching the live Container App.

Layer 1 makes `quotes-api` unreachable by anything except a request genuinely bearing a token this
Function's Managed Identity obtained. Layer 2 then decides, among requests that clear Layer 1, who
is allowed to do what. Neither is a substitute for the other.

## Real deployed endpoints

| What | URL |
|---|---|
| **Live Static Web App** (the one and only entry point) | `https://gentle-river-0e0339700.7.azurestaticapps.net` |
| Real Week-1 API — list quotes | `GET  /api/quotes?page=N&size=N` (via the SWA, proxied through the Function to the Container App) |
| Real Week-1 API — single quote | `GET  /api/quotes/{id}` |
| Real Week-1 API — create quote (requires the user's JWT) | `POST /api/quotes` |
| Real Week-1 API — login | `POST /api/auth/login` |
| `quotes-api` Container App's own hostname (not directly reachable anymore) | `https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io` |
| Function App's own hostname (not directly reachable anymore either) | `https://func-quotes-bff-day17.azurewebsites.net` |

Both backend hostnames now return `401 Unauthorized` if called directly — the SWA is the only
externally-usable path into this system. Custom domain: **still not available** —
`az staticwebapp hostname list` returns `[]`. Nothing invented here.

## Azure resources

| Resource | Name | Role |
|---|---|---|
| Static Web App | `swa-quotes-day17` (East Asia, Standard plan) | Hosts the SPA; linked backend now points at the Function, not the Container App |
| Function App | `func-quotes-bff-day17` (Central India, Flex Consumption, .NET 9 isolated) | The BFF; holds the system-assigned Managed Identity |
| Storage account | `stquotesbffday17` (Central India) | Functions runtime plumbing only, no app data |
| Application Insights | `appi-quotes-bff-day17` | Diagnostic only — telemetry didn't reliably land during testing (see Known limitations); not load-bearing for any proof in this README |
| Entra ID App Registration | `quotes-api-day17-mi` (`appId 9ec5db29-9812-43ac-9d81-8817e84b6cd0`) | Represents `quotes-api` as a resource/audience. `passwordCredentials: []` — no client secret exists for it, ever |
| Container App | `quotes-api` (Central India, existing since Day 5) | Unchanged business logic; new revision adds the `X-User-Token` fallback; built-in Entra auth configured, no secret |

## What changed in this repo (cumulative across this task)

Two earlier iterations (CORS + absolute API URL, then SWA-linked-backend-only) were fully reverted
once superseded — `day - 2/QuotesApi`'s core files and `day - 16/task - 2`'s API client files carry
**zero diff** against their pre-Day-17 committed state except for what's listed below. What's
actually left:

- `day - 16/task - 2/src/app/app.config.ts` — `HttpClient` switched to the Fetch backend
  (`withFetch()`). Same interceptor chain, same behavior; this measurably cut Total Blocking Time
  (see Lighthouse) with zero functional change (all 33 existing tests still pass).
- `day - 2/QuotesApi/Extensions/ProgramExtensions.cs` — the additive `X-User-Token` fallback
  described above (~20 lines).
- `day - 17/task - 1/quotes-bff/` — the new Function project (`Program.cs`, `QuotesApiProxy.cs`,
  `quotes-bff.csproj`, `host.json`).
- `day - 16/task - 2/public/staticwebapp.config.json`, `day - 16/task - 2/src/index.html`
  (meta description), `.github/workflows/deploy-day17-swa.yml` — carried over from earlier, still
  needed regardless of backend approach.

## Verification performed (all against the live, deployed system)

**Direct proof of the Managed Identity hop** — the Function's real token, decoded and exposed as
response headers (never the raw token itself):
```
GET https://func-quotes-bff-day17.azurewebsites.net/api/quotes   (tested before the Function itself
                                                                   was also SWA-link-locked down)
-> 200 OK
   X-Bff-Auth: managed-identity
   X-Mi-Token-Aud: api://9ec5db29-9812-43ac-9d81-8817e84b6cd0        <- exactly quotes-api's own App ID URI
   X-Mi-Token-Iss: https://sts.windows.net/8d46a076-.../               <- our tenant
   X-Mi-Token-Appid: 00661bb9-43dc-4778-8513-fb9c47239188            <- exactly the Function's own service principal appId
```

**Full chain, through the real SWA URL, fresh browser session (Playwright/headless Chromium):**
```
GET  /api/quotes                                   -> 200, X-Bff-Auth: managed-identity, real MI headers present
GET  /api/quotes/1                                 -> 404 (contract unchanged)
POST /api/auth/login {test@example.com/...}        -> 200, real HS256 JWT issued
POST /api/quotes (no Authorization)                 -> 401
POST /api/quotes (Authorization: Bearer <user JWT>) -> 201 Created, quote persisted, visible on next GET
```
Visiting `/quotes/new` while signed out still redirects to `/login?returnUrl=...` — the real route
guard, exercised in an actual browser, not asserted from source.

**Direct-access lockout, confirmed on both backend hostnames independently:**
```
GET https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/api/quotes  -> 401
GET https://quotes-api.victoriousbay-dc87b4fa.centralindia.azurecontainerapps.io/health       -> 401
GET https://func-quotes-bff-day17.azurewebsites.net/api/quotes                                -> 401
```
Only traffic that genuinely transits the SWA gets through to either backend resource.

**Regression testing before any live change:** the `X-User-Token` fallback was built, unit-tested
(38/38 `.NET` tests unchanged), and smoke-tested in local Docker — confirming `Authorization`-only
requests (the old behavior) and `X-User-Token` requests (the new behavior) both produce identical
`201 Created` results, and requests with neither still 401 — *before* it was deployed to the live
Container App as a new revision at 0% traffic, verified, then shifted to 100%.

**Final full re-verification, after removing the stale identity provider (see below):**
production build (`ng build --configuration production`, clean), 33/33 Angular tests, 38/38 .NET
tests, and the entire functional chain above re-run end-to-end with identical results.

**No secrets anywhere**: `quotes-api`'s secret list is unchanged (`jwt-key`,
`appinsights-connection-string` — the same two as before this task, nothing new); the Function
App's settings contain only non-secret configuration (`QuotesApi__BaseUrl`, `QuotesApi__AppIdUri`)
plus standard connection strings for its own plumbing (never committed); the Entra app registration
has `passwordCredentials: []`; the full git diff was scanned and contains no credential material.

## Cleanup performed this round: the stale `azureStaticWebApps` provider

`quotes-api` still had the `azureStaticWebApps` identity provider from when the SWA linked to it
*directly* (before the Function existed). Since the SWA's backend link now points at the Function
instead, that provider's trust condition — "this request was proxied through
`gentle-river-0e0339700.7.azurestaticapps.net`" — can never be satisfied for `quotes-api` again by
any real traffic path. Confirmed dead, removed via a direct config PUT
(`az rest --method put .../authConfigs/current`), leaving only the `azureActiveDirectory` (Entra/MI)
provider. **Verified the Managed Identity path still works immediately after**, including a
required revision restart to pick up the change (Container Apps' auth config isn't hot-reloaded
into a running instance — the same lesson learned earlier when first enabling this provider).

## Lighthouse — final results (`/quotes`, real deployed URL, post-cleanup)

| Category | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Performance | 98 | 98 | 99 |
| Accessibility | 100 | 100 | 100 |
| Best Practices | 100 | 100 | 100 |
| SEO | 100 | 100 | 100 |

All four categories clear the ≥95 bar on every run. This is a real improvement from an earlier
59–89 range across this task's iterations — driven by two genuine, correctness-neutral changes:
switching `HttpClient` to the Fetch backend (cut Total Blocking Time roughly in half) and removing
the CORS-preflight round trip once traffic became same-origin. The extra network hop this final
architecture adds (SWA → Function → Container App, versus the previous SWA → Container App direct)
did not regress performance.

## Known limitations

- **No app-role restriction on the Entra app registration** (`appRoleAssignmentRequired: false`):
  any identity in the Amity tenant that knows the Application ID URI could, in principle, request a
  token for it. Tightening this needs an app role assignment + admin consent — only self-serviceable
  with help from a tenant admin, so it was deliberately left at the default (fully self-serviceable
  without escalation).
- **Application Insights for the Function** (`appi-quotes-bff-day17`) didn't reliably receive
  telemetry during testing (likely a Flex Consumption short-execution flush characteristic) —
  verification relied on direct HTTP response-header evidence instead, which is arguably stronger
  proof anyway, but the App Insights resource itself isn't pulling its weight currently.
- **`quotes-api` still has no persistent volume** — a fresh revision always starts with an empty,
  freshly-migrated SQLite database (seeded users only). Predates this task, unrelated to the
  Managed Identity work.
- **New ongoing cost surface**: the Function App and its Storage Account are real resources that
  didn't exist before this task (likely near-$0 given Azure for Students' free grants, but real).

## Rollback

- Prior working revisions remain provisioned (not deactivated — deactivation was shown in an
  earlier round of this task to not reliably persist) at 0% traffic:
  `quotes-api--day17linked` (SWA-linked-only, no MI), `quotes-api--day17cors` (CORS-based).
- Full pre-MI auth config backed up before every change this round
  (`quotes-api-auth-before-mi.json`, `quotes-api-auth-before-cleanup.json`).
- To fully revert: re-link the SWA backend to `quotes-api` directly, shift Container App traffic to
  an earlier revision, and restore its auth config from the backup JSON via
  `az rest --method put .../authConfigs/current`.

## Mistakes caught and fixed across this task (kept for the record)

- **Git Bash path mangling**: a leading `/subscriptions/...` resource ID was silently rewritten
  into a broken Windows path by Git Bash's auto path-conversion, causing a confusing "Not Found"
  from `az staticwebapp backends link`. Fixed with `MSYS_NO_PATHCONV=1`.
- **A storage account key briefly appeared in my own tool output** while inspecting Function App
  settings. Rotated both keys immediately as a precaution, even though the account holds no
  application data.
- **PowerShell's `Compress-Archive` produces backslash-separated zip paths on Windows**, which
  Azure's Linux-based Function deployment validator rejects outright
  (`Cannot find required .azurefunctions directory`) — silently broke the Consumption-plan
  deployment for over an hour before a Flex Consumption redeploy surfaced the real error message.
  Fixed by building the zip via `System.IO.Compression.ZipFile` directly with forced forward-slash
  entry names.
- **Deactivating a Container App revision is not a durable rollback point** (learned in the
  previous round, reconfirmed here by deliberately leaving revisions merely at 0% traffic instead).
- **Container Apps' built-in auth config changes are not hot-reloaded** — every auth config change
  in this round needed an explicit revision restart before it took effect, confirmed twice (once
  when first enabling the Entra provider, once after removing the stale one).

## Final git status (before this round's commit)

```
 M day - 16/task - 2/src/app/app.config.ts
 M day - 2/QuotesApi/Extensions/ProgramExtensions.cs
?? day - 17/task - 1/quotes-bff/
```
