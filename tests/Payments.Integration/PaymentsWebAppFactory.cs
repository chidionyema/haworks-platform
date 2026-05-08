using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using Haworks.BuildingBlocks.Messaging;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Payments.Application.Consumers;

namespace Haworks.Payments.Integration;

/// <summary>
/// Custom WebApplicationFactory that wires up:
/// 1. A real PostgreSQL container (Testcontainers).
/// 2. An in-memory MassTransit harness (replaces RabbitMQ).
/// 3. In-memory configuration overrides.
/// </summary>
public class PaymentsWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .WithDatabase("payments")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public const string TestStripeSecret = "whsec_test";

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        // Ensure schema exists and migrations are run
        await EnsureSchemaAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:payments"] = _dbContainer.GetConnectionString(),
                // Fix: PaymentProviderOptions expects keys under the SectionName "PaymentProviders"
                ["PaymentProviders:Stripe:WebhookSecret"] = TestStripeSecret,
                ["PaymentProviders:Active"] = "Stripe"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Production AddMassTransit + AddDomainEventPublisher are skipped
            // by AddInfrastructure when ASPNETCORE_ENVIRONMENT=Test. Wire the
            // in-memory test harness + the consumer so we can assert that
            // (a) the controller publishes PaymentWebhookValidatedEvent, and
            // (b) the consumer publishes PaymentCompletedEvent in response.
            services.AddMassTransitTestHarness(mt =>
            {
                mt.AddConsumer<PaymentWebhookValidatedConsumer>();
            });
            services.AddDomainEventPublisher();
            services.AddSingleton<ITelemetryService>(_ => NullTelemetryService.Instance);
            
            // Fix: Explicitly register ResiliencePolicyFactory to satisfy implementation dependencies
            services.AddSingleton<IResiliencePolicyFactory, ResiliencePolicyFactory>();
        });
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<Haworks.Payments.Infrastructure.PaymentDbContext>();
        
        // Explicitly create schema before MigrateAsync checks for history table
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS payments;");
        await db.Database.MigrateAsync();
    }

    /// <summary>Convenience: build a Stripe-Signature header for a given payload.</summary>
    public static string SignStripe(string rawPayload, DateTimeOffset? at = null, string? secret = null)
    {
        var unix = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var signed = $"{unix}.{rawPayload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret ?? TestStripeSecret));
        var hex = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        return $"t={unix},v1={hex}";
    }
}
