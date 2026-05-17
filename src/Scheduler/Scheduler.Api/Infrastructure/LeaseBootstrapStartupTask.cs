using Haworks.Scheduler.Application.Common.Interfaces;
using Haworks.Scheduler.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Haworks.Scheduler.Api.Infrastructure;

/// <summary>
/// IHostedService that seeds VaultLease rows on Scheduler startup.
/// Ensures one row per service/role/type triple exists (upsert semantics).
/// </summary>
public sealed class LeaseBootstrapStartupTask : IHostedService
{
    private static readonly (string ServiceName, string RoleName, string CredentialType, TimeSpan Ttl)[] ServiceDefinitions =
    [
        ("catalog", "catalog-role", "database", TimeSpan.FromHours(24)),
        ("orders", "orders-role", "database", TimeSpan.FromHours(24)),
        ("payments", "payments-role", "database", TimeSpan.FromHours(24)),
        ("checkout", "checkout-role", "database", TimeSpan.FromHours(24)),
        ("identity", "identity-role", "database", TimeSpan.FromHours(24)),
        ("content", "content-role", "database", TimeSpan.FromHours(24)),
        ("notifications", "notifications-role", "database", TimeSpan.FromHours(24)),
        ("search", "search-role", "database", TimeSpan.FromHours(24)),
        ("scheduler", "scheduler-role", "database", TimeSpan.FromHours(24)),
        ("webhooks", "webhooks-role", "database", TimeSpan.FromHours(24)),
        // PKI certificates (30-day TTL)
        ("catalog", "catalog-role", "pki", TimeSpan.FromHours(720)),
        ("orders", "orders-role", "pki", TimeSpan.FromHours(720)),
        ("payments", "payments-role", "pki", TimeSpan.FromHours(720)),
        ("checkout", "checkout-role", "pki", TimeSpan.FromHours(720)),
        ("identity", "identity-role", "pki", TimeSpan.FromHours(720)),
        ("content", "content-role", "pki", TimeSpan.FromHours(720)),
        ("notifications", "notifications-role", "pki", TimeSpan.FromHours(720)),
    ];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LeaseBootstrapStartupTask> _logger;

    public LeaseBootstrapStartupTask(
        IServiceProvider serviceProvider,
        ILogger<LeaseBootstrapStartupTask> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        var added = 0;
        foreach (var (serviceName, roleName, credentialType, ttl) in ServiceDefinitions)
        {
            var exists = await repo.LeaseExistsAsync(serviceName, roleName, credentialType, cancellationToken)
                .ConfigureAwait(false);

            if (exists) continue;

            var lease = VaultLease.Create(serviceName, roleName, credentialType, ttl);
            await repo.AddLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
            added++;
        }

        if (added > 0)
        {
            await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("LeaseBootstrapStartupTask: seeded {Count} new VaultLease rows", added);
        }
        else
        {
            _logger.LogDebug("LeaseBootstrapStartupTask: all lease rows already exist");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
