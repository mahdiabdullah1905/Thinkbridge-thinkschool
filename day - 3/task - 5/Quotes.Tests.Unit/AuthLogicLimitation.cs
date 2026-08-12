namespace Quotes.Tests.Unit;

/// <summary>
/// LIMITATION REPORT: Refresh-Token Reuse Detection
/// 
/// The requirement to unit-test the authentication refresh-token reuse-detection logic
/// cannot be fulfilled natively at the unit level.
/// 
/// Reason: The reuse detection logic is currently embedded directly inside an anonymous
/// endpoint closure (lambda) mapped to the "/refresh" endpoint in `day - 2/QuotesApi/Extensions/ProgramExtensions.cs`.
/// Because it is an inline lambda bound directly to the routing pipeline, it is impossible
/// to invoke or test this logic without either:
///   1. Spinning up a test server and doing integration testing via WebApplicationFactory/HttpClient.
///   2. Using brittle reflection to extract the raw Delegate from the ASP.NET Core Endpoint metadata.
/// 
/// Per the strict project constraints, we are forbidden from modifying the Day 2 production code
/// just to force a test structure (e.g., extracting the lambda into a public static method or service).
/// We are also constrained to pure unit-testing techniques (no WebApplicationFactory or TestServer).
/// 
/// Therefore, the refresh-token reuse-detection tests have been explicitly omitted from this suite
/// to avoid faking coverage, duplicating production logic, or violating architectural constraints.
/// The 20+ unit test requirement is fully satisfied by thoroughly testing the genuinely unit-testable
/// components (Validators, Quote.Create factory, and Clock).
/// </summary>
public static class AuthLogicLimitation
{
}
