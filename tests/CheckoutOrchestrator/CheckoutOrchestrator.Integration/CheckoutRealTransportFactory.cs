using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Haworks.CheckoutOrchestrator.Infrastructure;
using Haworks.BuildingBlocks.Testing;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;

namespace Haworks.CheckoutOrchestrator.Integration;

/// <summary>
/// Factory that uses REAL RabbitMQ (Testcontainers) instead of the
/// in-memory test harness. This is the production-equivalent test setup.
/// The saga, EF outbox, RabbitMQ transport, and consumer scope isolation
/// are all exercised exactly as they are in production.
/// </summary>
public sealed class CheckoutRealTransportFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _pgConn = string.Empty;
    private string _rabbitConn = string.Empty;

    public async Task InitializeAsync()
    {
        _pgConn = await SharedTestPostgres.CreateDatabaseAsync("checkout-rt");
        _rabbitConn = await SharedTestRabbitMq.GetConnectionStringAsync();

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Integration");
        Environment.SetEnvironmentVariable("ConnectionStrings__checkout", _pgConn);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", _rabbitConn);
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("Checkout__SuccessUrl", "http://localhost/success");
        Environment.SetEnvironmentVariable("Checkout__CancelUrl", "http://localhost/cancel");

        // Purge stale messages from the reused RabbitMQ container so old
        // outbox-delivered messages don't overwhelm the consumer on startup.
        await PurgeRabbitMqQueuesAsync();

    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Integration");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:checkout"] = _pgConn,
                ["ConnectionStrings:rabbitmq"] = _rabbitConn,
                ["Vault:Enabled"] = "false",
                ["Checkout:SuccessUrl"] = "http://localhost/success",
                ["Checkout:CancelUrl"] = "http://localhost/cancel",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // NO AddMassTransitTestHarness — use the REAL MassTransit + RabbitMQ
            // The app's DI already registers MassTransit with UsingRabbitMq.
            // We just need auth for the [Authorize] endpoints.
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();

            // Run migrations BEFORE any IHostedService (including MassTransit's bus).
            // IStartupFilter runs during app pipeline build, before hosted services start.
            services.AddTransient<IStartupFilter, MigrationStartupFilter<CheckoutDbContext>>();
        });
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        await db.Database.MigrateAsync();

        // Purge stale outbox messages from previous test runs so the
        // BusOutboxDeliveryService doesn't spend time delivering old messages
        // before getting to the test's message.
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """TRUNCATE TABLE checkout."OutboxMessage", checkout."OutboxState" CASCADE""");
        }
        catch { /* tables may not exist on first run */ }
    }

    private async Task PurgeRabbitMqQueuesAsync()
    {
        // The SharedTestRabbitMq container uses rabbitmq:3-management.
        // Parse the AMQP connection string to get the host/port, then
        // use the management API (port 15672 mapped inside the container)
        // to purge all queues.
        var uri = new Uri(_rabbitConn);
        // Management API runs on the port offset by +10000 from AMQP (5672 → 15672).
        // Testcontainers maps both ports; the mapped management port = mapped AMQP port isn't
        // predictable, so we query it via the container.
        // Simpler: use RabbitMQ.Client to purge directly via AMQP.
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory { Uri = uri };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            // Purge the saga queue if it exists
            try { await channel.QueuePurgeAsync("checkout-saga-state"); } catch { }
            // Also purge the error queue
            try { await channel.QueuePurgeAsync("checkout-saga-state_error"); } catch { }
        }
        catch
        {
            // Queue might not exist yet on first run — safe to ignore
        }
    }
}
