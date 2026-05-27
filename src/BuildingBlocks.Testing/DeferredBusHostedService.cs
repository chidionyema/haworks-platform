using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Testing;

/// <summary>
/// Replaces MassTransit's default bus hosted service in integration tests.
/// Runs EF MigrateAsync FIRST, then starts the MassTransit bus.
/// This prevents the race condition where saga consumers query the DB
/// before migrations have run.
///
/// Usage in test factory ConfigureTestServices:
///   services.AddDeferredBusStart&lt;MyDbContext&gt;();
/// </summary>
public sealed class DeferredBusHostedService<TDbContext>(
    IBusControl bus,
    IServiceProvider sp,
    ILogger<DeferredBusHostedService<TDbContext>> logger)
    : IHostedService
    where TDbContext : DbContext
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Phase 1: Migrate
        logger.LogInformation("DeferredBusStart: migrating {DbContext} before bus start",
            typeof(TDbContext).Name);

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
#pragma warning disable HWK027, EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA IF NOT EXISTS {schema};", ct);
#pragma warning restore HWK027, EF1002
        }

        await db.Database.MigrateAsync(ct);

        logger.LogInformation("DeferredBusStart: migration complete, starting bus");

        // Phase 2: Start bus
        await bus.StartAsync(ct);

        logger.LogInformation("DeferredBusStart: bus started");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await bus.StopAsync(ct);
    }
}

public static class DeferredBusExtensions
{
    /// <summary>
    /// Removes MassTransit's default bus hosted service and replaces it with
    /// one that runs MigrateAsync before starting the bus.
    /// </summary>
    public static IServiceCollection AddDeferredBusStart<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        // Remove MassTransit's auto-registered hosted service
        var mtHosted = services.Where(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.FullName?.Contains("MassTransitHostedService") == true)
            .ToList();

        foreach (var d in mtHosted)
            services.Remove(d);

        // Add our deferred version
        services.AddSingleton<IHostedService, DeferredBusHostedService<TDbContext>>();

        return services;
    }
}
