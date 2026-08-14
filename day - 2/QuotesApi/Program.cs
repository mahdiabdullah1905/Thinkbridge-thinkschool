using Microsoft.EntityFrameworkCore;
using Serilog;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using OpenTelemetry.Trace;
using QuotesApi.Configuration;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// CreateBuilder only auto-loads User Secrets when EnvironmentName == "Development";
// load explicitly so Task 7's Jwt:Key still resolves under the "Testing" environment.
builder.Configuration.AddUserSecrets<Program>();

var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri) && !builder.Environment.IsEnvironment("Testing"))
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

// Prevents duplicate console output: Serilog owns the console sink below,
// and forwards into ILoggingBuilder providers only for the Azure Monitor exporter.
builder.Logging.ClearProviders();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext(), writeToProviders: true);

builder.Services.AddTransient<TraceIdMiddleware>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);

// Registered for future outbound calls (e.g. a third-party quotes source, Entra ID); no endpoint
// consumes it yet. See Extensions/ResilienceExtensions.cs.
builder.Services.AddResilientExternalServiceClient();

var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("QuotesApi")
               .AddAspNetCoreInstrumentation()
               .AddEntityFrameworkCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddOtlpExporter()
               .AddConsoleExporter();
    });

var appInsightsConnectionString = builder.Configuration["AppInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    openTelemetryBuilder.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}

var app = builder.Build();

app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.MapGet("/health", () => new { status = "healthy" });

app.Run();

public partial class Program { }