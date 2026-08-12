using QuotesApi.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Reuse Day 2 setup (registers SQLite DB and "Bearer" self-issued JWT scheme)
builder.Services.AddInfrastructure(builder.Configuration);

// 2. Override default scheme and add Entra ID support
builder.Services.AddAuthentication(defaultScheme: "SmartPolicy")
    .AddPolicyScheme("SmartPolicy", "Smart Policy", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    // Route to Entra scheme if issued by Microsoft
                    if (jwt.Issuer.Contains("login.microsoftonline.com") || jwt.Issuer.Contains("sts.windows.net"))
                    {
                        return "Entra";
                    }
                }
            }
            // Fall back to Day 2 self-issued scheme
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer("Entra", options =>
    {
        var tenantId = builder.Configuration["Entra:TenantId"];
        var audience = builder.Configuration["Entra:Audience"];
        
        if (!string.IsNullOrEmpty(tenantId))
        {
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuers = new[] 
                { 
                    $"https://login.microsoftonline.com/{tenantId}/v2.0", 
                    $"https://sts.windows.net/{tenantId}/" 
                },
                ValidAudience = audience
            };
        }
        
        options.Audience = audience;
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<QuotesApi.Data.AppDbContext>();
    dbContext.Database.Migrate();
}

// 3. Reuse Day 2 route mappings
app.MapAuthEndpoints(builder.Configuration);
app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.Run();
