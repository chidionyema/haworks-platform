using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Orders.Application.Consumers;

namespace Haworks.Orders.Integration;

/// <summary>
/// WebApplicationFactory for orders-svc integration tests.
/// Same pattern as catalog/payments: Testcontainers postgres + in-memory
/// MassTransit harness with all 3 consumers wired so we can publish upstream
/// events into the harness and assert state + outbound publishes.
/// </summary>
public sealed class OrdersWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await SharedTestPostgres.CreateDatabaseAsync("orders");
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__orders", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", "amqp://guest:guest@localhost:5672/");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");

        // Migrate BEFORE any test accesses Services (which boots the host).
        // The EF Outbox filter writes to OutboxMessage/InboxState on the first
        // consumed message — tables must exist before consumers start.
        await EnsureSchemaAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Shared Postgres container outlives the fixture intentionally.
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:orders"]   = ConnectionString,
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                ["Vault:Enabled"]              = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove outbox-related hosted services — the test harness uses
            // in-memory transport and doesn't need outbox delivery/cleanup.
            var outboxServices = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType?.FullName != null &&
                            (d.ImplementationType.FullName.Contains("BusOutboxDelivery") ||
                             d.ImplementationType.FullName.Contains("InboxCleanup") ||
                             d.ImplementationType.FullName.Contains("OutboxCleanup") ||
                             d.ImplementationType.FullName.Contains("OutboxDelivery")))
                .ToList();
            foreach (var d in outboxServices)
                services.Remove(d);

            services.AddMassTransitTestHarness(mt =>
            {
                mt.AddConsumer<PaymentCompletedConsumer>();
                mt.AddConsumer<PaymentSessionFailedConsumer>();
                mt.AddConsumer<StockReservationFailedConsumer>();
                mt.AddConsumer<CheckoutSessionExpiredConsumer>();
                mt.AddConsumer<RefundCompletedConsumer>();
                mt.AddConsumer<RefundCancelledConsumer>();
                mt.AddConsumer<PrivacyErasureRequestedConsumer>();
            });

            // [Authorize]-decorated endpoints need an authentication scheme.
            // BuildingBlocks.Testing's TestAuthenticationHandler is a no-op
            // handler that auto-authenticates as a fixed test user.
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Haworks.Orders.Infrastructure.OrderDbContext>();
        await db.Database.MigrateAsync();
    }
}
