using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Refit;
using SafeDiskCleaner.Core.Abstractions;
using SafeDiskCleaner.Infrastructure.Data;
using SafeDiskCleaner.Infrastructure.Services;
using Serilog;

namespace SafeDiskCleaner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSafeDiskInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IReportWriter, ReportWriter>();

        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IQuarantineService, QuarantineService>();

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            var connection = $"Data Source={Path.Combine(paths.DataRoot, "SafeDisk.db")}";
            options.UseSqlite(connection);
        });

        services.AddHttpClient("github", client =>
            {
                client.BaseAddress = new Uri("https://api.github.com");
                client.Timeout = TimeSpan.FromSeconds(8);
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<IGitHubApi>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient("github");
            return RestService.For<IGitHubApi>(httpClient);
        });

        return services;
    }

    public static IServiceCollection AddSafeDiskDatabase(this IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var paths = provider.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        return services;
    }

    public static IHostBuilder UseSerilogDefaults(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, services, configuration) =>
        {
            var paths = services.GetRequiredService<IAppPaths>();
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(paths.DataRoot, "logs", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));
        });
        return hostBuilder;
    }
}
