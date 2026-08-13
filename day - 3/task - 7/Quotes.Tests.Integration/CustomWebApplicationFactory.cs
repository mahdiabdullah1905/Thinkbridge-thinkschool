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

            // Targeted removal of SQLite-specific EF Core registrations
            var sqliteServices = services.Where(d => 
                d.ImplementationType != null && 
                d.ImplementationType.FullName != null &&
                d.ImplementationType.FullName.Contains("Sqlite")
            ).ToList();

            foreach (var service in sqliteServices)
            {
                services.Remove(service);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(_dbContainer.GetConnectionString());
                options.ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrator, EnsureCreatedMigrator>();
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

public class EnsureCreatedMigrator : Microsoft.EntityFrameworkCore.Migrations.IMigrator
{
    private readonly Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator _creator;
    public EnsureCreatedMigrator(Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator creator)
    {
        _creator = creator;
    }

    public void Migrate(string? targetMigration = null) => _creator.EnsureCreated();
    public System.Threading.Tasks.Task MigrateAsync(string? targetMigration = null, System.Threading.CancellationToken cancellationToken = default) => _creator.EnsureCreatedAsync(cancellationToken);
    public string GenerateScript(string? fromMigration = null, string? toMigration = null, Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions options = Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Default) => "";
    public string GenerateScript(string? fromMigration = null, string? toMigration = null, Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions options = Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Default, string? idempotentScript = null) => "";
    public bool HasPendingModelChanges() => false;
}

[CollectionDefinition("SharedTestCollection")]
public class SharedTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}
