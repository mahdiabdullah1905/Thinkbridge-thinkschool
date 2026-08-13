using Microsoft.EntityFrameworkCore;
using Serilog;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using OpenTelemetry.Trace;
using QuotesApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTransient<TraceIdMiddleware>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("QuotesApi")
               .AddAspNetCoreInstrumentation()
               .AddEntityFrameworkCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddOtlpExporter()
               .AddConsoleExporter();
    });

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

app.Run();

public partial class Program { }