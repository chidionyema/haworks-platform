using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Haworks.BuildingBlocks.Persistence;

public static class DatabaseMigrationExtensions
{
    public static void MigrateDatabase<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        if (app.Environment.IsEnvironment("Test")) return;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();
        var policy = BuildPolicy(logger);
        policy.ExecuteAsync(async ct => await db.Database.MigrateAsync(ct), CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public static Task MigrateWithRetryAsync(
        this DatabaseFacade database,
        ILogger logger,
        CancellationToken ct = default)
    {
        var policy = BuildPolicy(logger);
        return policy.ExecuteAsync(async (innerCt) =>
        {
            await database.MigrateAsync(innerCt);
        }, ct);
    }

    private static AsyncRetryPolicy BuildPolicy(ILogger logger) =>
        Policy
            .Handle<Exception>(IsTransientPostgresStartup)
            .WaitAndRetryAsync(
                retryCount: 8,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 5))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)),
                onRetry: (ex, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        ex,
                        "EF migration retry {Attempt}/8 after {Delay}ms — {ExceptionType}: {Message}",
                        attempt, (int)delay.TotalMilliseconds,
                        ex.GetType().Name, ex.Message);
                });

    private static bool IsTransientPostgresStartup(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.Equals(current.GetType().Name, "PostgresException", StringComparison.Ordinal))
            {
                var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
                if (sqlState is "57P03" or "57P02" or "08006" or "08001")
                    return true;
            }

            var typeName = current.GetType().Name;
            if (typeName is "SocketException" or "NpgsqlException")
            {
                if (current.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("server closed the connection", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("starting up", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
