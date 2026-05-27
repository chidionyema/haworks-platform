using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.BuildingBlocks.Testing;

/// <summary>
/// Runs EF MigrateAsync during app startup, BEFORE any IHostedService starts.
/// This ensures the DB schema exists before MassTransit's bus begins consuming.
/// </summary>
public sealed class MigrationStartupFilter<TDbContext> : IStartupFilter
    where TDbContext : DbContext
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            using var scope = app.ApplicationServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var schema = db.Model.GetDefaultSchema();
            if (!string.IsNullOrEmpty(schema))
            {
#pragma warning disable HWK027, EF1002
                db.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS {schema};");
#pragma warning restore HWK027, EF1002
            }

            db.Database.Migrate();

            next(app);
        };
    }
}
