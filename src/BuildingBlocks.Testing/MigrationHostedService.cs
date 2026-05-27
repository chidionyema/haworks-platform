using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Haworks.BuildingBlocks.Testing;

/// <summary>
/// Runs EF MigrateAsync before any other IHostedService (including MassTransit's bus).
/// Register via services.Insert(0, ...) to ensure it runs first.
/// </summary>
public sealed class MigrationHostedService<TDbContext>(IServiceProvider sp) : IHostedService
    where TDbContext : DbContext
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Create schema if DbContext uses HasDefaultSchema.
        // Schema name is from our own code (not user input) so safe to concatenate.
#pragma warning disable HWK027, EF1002
        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA IF NOT EXISTS {schema};", ct);
        }
#pragma warning restore HWK027, EF1002

        await db.Database.MigrateAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
