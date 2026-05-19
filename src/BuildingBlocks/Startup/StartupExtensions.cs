using Haworks.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Startup;

public static class StartupExtensions
{
    private static readonly string[] ReadyTags = ["ready"];

    public static IServiceCollection AddStartupTaskRunner(this IServiceCollection services)
    {
        services.AddSingleton<StartupTaskRunner>();
#pragma warning disable HWK081
        services.AddHostedService(sp => sp.GetRequiredService<StartupTaskRunner>());
#pragma warning restore HWK081
        services.AddHealthChecks().AddCheck<StartupReadinessHealthCheck>("startup", tags: ReadyTags);
        return services;
    }

    public static StartupTaskRunner AddMigrationTask<TContext>(this StartupTaskRunner runner) where TContext : DbContext
    {
        runner.AddTask(async (sp, ct) =>
        {
            using var scope = sp.CreateScope();
#pragma warning disable HWK081
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();
            await db.Database.MigrateWithRetryAsync(logger, ct);
        });
        return runner;
    }
#pragma warning restore HWK081
}
