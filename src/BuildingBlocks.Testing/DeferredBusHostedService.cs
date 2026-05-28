using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Testing;

public sealed class DeferredBusHostedService<TDbContext>(
    IBusControl bus,
    IServiceProvider sp,
    ILogger<DeferredBusHostedService<TDbContext>> logger)
    : IHostedService
    where TDbContext : DbContext
{
    public async Task StartAsync(CancellationToken ct)
    {
        logger.LogInformation("DeferredBusStart: migrating {DbContext}", typeof(TDbContext).Name);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
#pragma warning disable HWK027, EF1002
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS {schema};", ct);
#pragma warning restore HWK027, EF1002
        }
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("DeferredBusStart: starting bus");
        await bus.StartAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct) => await bus.StopAsync(ct);
}

public static class DeferredBusExtensions
{
    public static IServiceCollection AddDeferredBusStart<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        var mtHosted = services.Where(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.FullName?.Contains("MassTransitHostedService") == true)
            .ToList();
        foreach (var d in mtHosted) services.Remove(d);
        services.AddSingleton<IHostedService, DeferredBusHostedService<TDbContext>>();
        return services;
    }
}
