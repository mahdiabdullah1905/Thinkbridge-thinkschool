# Day 4 Task 6 - Connect to Azure Application Insights

This task wires the existing `QuotesApi` (day - 2/QuotesApi) OpenTelemetry pipeline (added in
[Day 4 Task 5](../task%20-%205/README.md)) up to a real Azure Application Insights resource, with the
connection string sourced from Azure Key Vault at runtime — never hardcoded, never committed.

## Azure resources

| Resource | Name | Region |
|---|---|---|
| Resource Group | `rg-quotesapi-monitoring` | Central India |
| Log Analytics Workspace | `law-quotesapi` | Central India |
| Application Insights (workspace-based) | `appi-quotesapi` | Central India |
| Key Vault (RBAC authorization) | `kv-quotesapi-t6` | Central India |
| Key Vault secret | `AppInsights--ConnectionString` | — |

Subscription: **Azure for Students** (tenant: Amity University). All resources created via `az` CLI, no
fake/placeholder resources.

## Connection setup

Packages added to `day - 2/QuotesApi/QuotesApi.csproj`:

- `Azure.Monitor.OpenTelemetry.AspNetCore`
- `Azure.Identity`
- `Azure.Extensions.AspNetCore.Configuration.Secrets`

`Program.cs` (excerpt — connection string is never present in source, only a Key Vault URI):

```csharp
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    // Managed Identity is only reachable on actual Azure-hosted compute; excluding it here
    // avoids a slow IMDS probe/failure on local dev and CI machines, falling straight through
    // to the developer's own `az login` session (AzureCliCredential).
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential = true
    });
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential);
}

builder.Logging.ClearProviders();
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext(), writeToProviders: true);

// ... existing Task 5 tracing setup (AddSource, ASP.NET Core / EF Core / HttpClient
// instrumentation, OTLP + Console exporters) is unchanged ...

var appInsightsConnectionString = builder.Configuration["AppInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    openTelemetryBuilder.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}
```

`appsettings.json` only carries the non-secret Key Vault URI (empty by default, so CI and anyone without
it configured runs exactly as before Task 6):

```json
"KeyVault": {
  "Uri": ""
}
```

For local development, the real Key Vault URI is set via User Secrets (same convention as Task 7's JWT
key), never committed:

```
dotnet user-secrets set "KeyVault:Uri" "https://kv-quotesapi-t6.vault.azure.net/"
```

At runtime, `AddAzureKeyVault` reads the secret named `AppInsights--ConnectionString` from Key Vault and
maps it to configuration key `AppInsights:ConnectionString` (Key Vault's `--` becomes `:`), which is then
handed to `UseAzureMonitor()`. The connection string itself never appears in any file in this repository.

Access model: your Azure AD identity was granted the `Key Vault Secrets User` (read-only) RBAC role on
`kv-quotesapi-t6`, so `DefaultAzureCredential` (via `az login`) can read the secret locally. A temporary
`Key Vault Secrets Officer` grant used to create the secret was revoked immediately after.

## Why this preserves Tasks 3-5 and 7

- OTLP + Console exporters (Task 5) are untouched — Azure Monitor is an additional exporter on the same
  `OpenTelemetryBuilder`, not a replacement.
- Serilog console logging (Task 4) is untouched in format/content. `builder.Logging.ClearProviders()` +
  `writeToProviders: true` only routes Serilog's events into the (now Azure-Monitor-only) `ILoggingBuilder`
  pipeline, avoiding duplicate console output while letting logs reach Application Insights.
- Everything is opt-in on configuration being present: with no `KeyVault:Uri` set (the default, and the
  case on any CI machine), the app behaves exactly as it did after Task 5 — confirmed by running the full
  test suite with zero Azure configuration present.
- Typed `JwtOptions` / User Secrets from Task 7 are untouched.

## Verification performed

- `dotnet test day - 2/QuotesApi.Tests` — **28/28 passed**, no Azure config present (CI-equivalent state).
- `dotnet test day - 3/task - 7/Quotes.Tests.Integration` — **20/20 passed** (Testcontainers SQL Server
  suite unaffected).
- Ran `QuotesApi` locally with the real `KeyVault:Uri` user secret set, logged in as the seeded
  `test@example.com` user, and issued a real `POST /api/quotes` request.
- Console output confirmed the `CreateQuote` span nested under `POST /api/quotes/`, both correlated by
  TraceId `2faf27c71935d18e5f27b382a025f7e0`, with resource attribute
  `telemetry.distro.name: Azure.Monitor.OpenTelemetry.AspNetCore` present (confirms Azure Monitor exporter
  is active in the pipeline).
- Queried Application Insights directly via `az monitor app-insights query` and confirmed the same
  operation landed:
  - `requests` table: `POST /api/quotes/`, resultCode `201`, duration `219.8ms`, `operation_Id`
    `2faf27c71935d18e5f27b382a025f7e0`.
  - `traces` table: application log lines `Successfully saved quote 7` and
    `Returning Created response for quote 7`, both tagged with the same `operation_Id`.

This confirms logs, traces, and request telemetry all reach Application Insights end-to-end, correlated by
the same identifier that ties them together in Serilog and OpenTelemetry locally.

Connection string and instrumentation key were never printed to the console or committed anywhere during
this process.

## KQL: slowest 10 requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, resultCode, duration, operation_Id, cloud_RoleName
```

## KQL: correlate all telemetry for a specific request (TraceId)

```kql
traces
| where timestamp > ago(15m)
| where operation_Id == "<TraceId>"
| order by timestamp asc
```

## Alert: POST /api/quotes average response time > 500ms over 5 minutes

**Created and enabled.** Standard Azure Monitor *metric* alerts on `requests/duration` don't support
filtering by request name (only `resultCode`, `success`, `performanceBucket`, `roleName`/`roleInstance`
dimensions are available on that platform metric) — confirmed via
`az monitor metrics list-definitions`. So this is a **log-based (scheduled query) alert** instead, which
runs a real KQL query on a schedule:

| Setting | Value |
|---|---|
| Type | Scheduled query rule (log alert) |
| Query | `requests \| where name == 'POST /api/quotes/' \| summarize AvgDuration=avg(duration)` |
| Condition | `avg('AvgDuration') > 500` |
| Evaluation window | 5 minutes |
| Frequency | Every 1 minute |
| Action Group | `ag-quotesapi-alerts` (email → `mahdi.abdullah@s.amity.edu`) |
| Alert rule name | `alert-quotes-post-latency-500ms` |
| Severity | Sev 3 (Warning) — this is a threshold worth investigating, not a page-worthy outage |

Rationale for email (not paging): a single slow `POST /api/quotes` over 5 minutes is worth a look, not a
wake-someone-up event — consistent with "alerts that page only when they need to be acted on."
