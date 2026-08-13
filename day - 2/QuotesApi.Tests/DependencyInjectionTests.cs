using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Repositories;
using QuotesApi.Services;
using QuotesApi.Middleware;

namespace QuotesApi.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void Singleton_IClock_ReturnsSameInstance()
    {
        var app = new TestingWebApplicationFactory();
        
        using var scope = app.Services.CreateScope();
        var clock1 = scope.ServiceProvider.GetRequiredService<IClock>();
        var clock2 = scope.ServiceProvider.GetRequiredService<IClock>();
        
        // They should be the exact same instance because it's a Singleton
        Assert.Same(clock1, clock2);
    }

    [Fact]
    public void Scoped_IQuoteRepository_ReturnsSameInstancePerScopeButDifferentAcrossScopes()
    {
        var app = new TestingWebApplicationFactory();
        
        using var scope1 = app.Services.CreateScope();
        var repo1a = scope1.ServiceProvider.GetRequiredService<IQuoteRepository>();
        var repo1b = scope1.ServiceProvider.GetRequiredService<IQuoteRepository>();
        
        // Same instance within the same scope
        Assert.Same(repo1a, repo1b);
        
        using var scope2 = app.Services.CreateScope();
        var repo2 = scope2.ServiceProvider.GetRequiredService<IQuoteRepository>();
        
        // Different instance in a different scope
        Assert.NotSame(repo1a, repo2);
    }

    [Fact]
    public void Transient_ExceptionHandlingMiddleware_ReturnsDifferentInstances()
    {
        var app = new TestingWebApplicationFactory();
        
        using var scope = app.Services.CreateScope();
        var middleware1 = scope.ServiceProvider.GetRequiredService<ExceptionHandlingMiddleware>();
        var middleware2 = scope.ServiceProvider.GetRequiredService<ExceptionHandlingMiddleware>();
        
        // Different instances every time because it's Transient
        Assert.NotSame(middleware1, middleware2);
    }
}
