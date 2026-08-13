# Day 4 Task 7: Configuration Done Right

## Overview
This task implements the Options pattern and externalizes secrets using ASP.NET Core User Secrets, ensuring that sensitive data is not hardcoded in the source code.

## Changes Made
1. **Typed Options Class:** Created `QuotesApi.Configuration.JwtOptions` to represent the `Jwt` configuration section.
2. **Options Pattern Binding:** Added `builder.Services.Configure<JwtOptions>(...)` in `Program.cs`.
3. **Migrated Configuration Access:** Updated `Extensions/ProgramExtensions.cs` to use `IOptionsSnapshot<JwtOptions>` for scoped resolution during request handling (e.g., when generating JWTs on `/login` and `/refresh`).
4. **Removed Hardcoded Secrets:** Stripped all hardcoded JWT fallback keys from `ProgramExtensions.cs`.
5. **User Secrets Implementation:** Initialized User Secrets on the project (`dotnet user-secrets init`) and stored the local development JWT key outside of source control (`dotnet user-secrets set "Jwt:Key" "..."`).

## Verification
- All tests pass successfully (`dotnet test`).
- No secrets are committed to source control (the `<UserSecretsId>` is added to the `.csproj`, but the secrets themselves live in the user profile).
- Observability and instrumentation (Serilog, OpenTelemetry) added in Day 4 Tasks 4 and 5 remain untouched and fully functional.
