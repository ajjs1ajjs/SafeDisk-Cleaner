using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Infrastructure.Data;

namespace SafeDiskCleaner.Infrastructure.Services;

/// <summary>
/// Creates the app data directories and the SQLite schema at startup using the
/// host's own service provider. Previously this ran eagerly inside
/// <c>AddSafeDiskDatabase</c> via a throwaway <c>BuildServiceProvider()</c>,
/// which leaked a second container and an undisposed scope (duplicate
/// singletons, unmanaged resources).
/// </summary>
public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceProvider _services;

    public DatabaseInitializer(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var paths = _services.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
