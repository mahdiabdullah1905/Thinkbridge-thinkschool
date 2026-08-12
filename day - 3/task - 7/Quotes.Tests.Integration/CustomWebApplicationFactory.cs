using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using QuotesApi.Data;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Services;
using System.Data.Common;
using System.Linq;
using Testcontainers.MsSql;
using System.Threading.Tasks;
using Xunit;

namespace Quotes.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer;

    public CustomWebApplicationFactory()
    {
        _dbContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            var dbConnectionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbConnection));
            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(_dbContainer.GetConnectionString());
            });

            var clockDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));
            if (clockDescriptor != null)
            {
                services.Remove(clockDescriptor);
            }

            services.AddSingleton<IClock, FakeClock>();
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM CollectionItem;
            DELETE FROM Collections;
            DELETE FROM Quotes;
            DELETE FROM RefreshTokens;
            DELETE FROM Users;
        ");
    }
}

[CollectionDefinition("SharedTestCollection")]
public class SharedTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}
