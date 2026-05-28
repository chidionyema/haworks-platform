using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Payments.Application.Consumers;
using Haworks.Payments.Api.Webhooks;
using Haworks.Payments.Application.Sagas;
using Haworks.Payments.Domain;
using Haworks.Payments.Infrastructure;
using Haworks.Payments.Infrastructure.Options;

namespace Haworks.Payments.Integration;

public class PaymentsWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnString { get; private set; } = string.Empty;
    public const string TestStripeSecret = "whsec_test_secret_1234567890abcdef";

    public async Task InitializeAsync()
    {
        ConnString = await SharedTestPostgres.CreateDatabaseAsync("payments");
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__payments", ConnString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", "amqp://guest:guest@localhost:5672/");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("Webhooks__Stripe__WebhookSecret", TestStripeSecret);
        Environment.SetEnvironmentVariable("PaymentProviders__Active", "Stripe");
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__WebhookSecret", TestStripeSecret);
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__SecretKey", "sk_test_dummy");

        _ = Services;
        await EnsureSchemaAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    public async Task EnsureSchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS payments;");
        await db.Database.MigrateAsync();
        // Drop outbox/inbox tables — they cause MassTransit's saga PublishAsync to
        // route through the EF outbox pipeline which faults on ctx.Init<T>() in
        // the in-memory test harness. The saga tests publish directly via IBus.
        try
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS payments.\"InboxState\", payments.\"OutboxMessage\", payments.\"OutboxState\" CASCADE");
        }
        catch { /* tables may not exist */ }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:payments"] = ConnString,
                ["Webhooks:Stripe:WebhookSecret"] = TestStripeSecret,
                ["PaymentProviders:Active"] = "Stripe",
                ["PaymentProviders:Stripe:WebhookSecret"] = TestStripeSecret,
                ["PaymentProviders:Stripe:SecretKey"] = "sk_test_dummy",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigureAll<WebhookOptions>(opt =>
            {
                opt.Stripe.WebhookSecret = TestStripeSecret;
            });

            services.PostConfigureAll<PaymentProviderOptions>(opt =>
            {
                opt.Active = Haworks.Contracts.Payments.PaymentProvider.Stripe;
                opt.Stripe.WebhookSecret = TestStripeSecret;
            });

            services.AddMassTransitTestHarness(mt =>
            {
                // No explicit scheduler needed — the Schedule() delay is passed inline
                // in the saga binder (not via s.Delay on the configuration), so MT
                // uses the in-memory transport's built-in scheduler.

                // Only register consumers that don't interfere with manual saga event flow.
                // ProviderRefundInitiationRequestedConsumer and SubscriptionRenewalRequestedConsumer
                // are omitted because they auto-process saga-published events (calling real Stripe)
                // and race with manually-published test events.
                mt.AddConsumer<PaymentWebhookValidatedConsumer>();
                mt.AddConsumer<PaymentSessionRequestedConsumer>();
                mt.AddSagaStateMachine<RefundSaga, RefundSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ExistingDbContext<PaymentDbContext>();
                        r.UsePostgres();
                    });
                mt.AddSagaStateMachine<SubscriptionSaga, SubscriptionSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ExistingDbContext<PaymentDbContext>();
                        r.UsePostgres();
                    });
                // No global SaveChanges filter here — saga state machines use
                // EntityFrameworkRepository which manages its own saves.
                // Webhook consumers that need SaveChanges get it via
                // PaymentWebhookValidatedConsumer calling SaveChangesAsync
                // on the repository at the end of processing.
            });

            services.AddSingleton<ITelemetryService>(_ => NullTelemetryService.Instance);
            services.AddSingleton<IResiliencePolicyFactory, ResiliencePolicyFactory>();

            // [Authorize]-decorated endpoints need an authentication scheme.
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }

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

/// <summary>
/// Test-only consume filter that calls SaveChangesAsync on the PaymentDbContext
/// after each consumer completes — but only if there are pending changes.
/// In production, the MassTransit EF Outbox handles this.
/// Saga state machines use EntityFrameworkRepository which calls SaveChanges
/// internally, leaving no pending changes for this filter.
/// </summary>
internal sealed class TestSaveChangesFilter<T>(PaymentDbContext db) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        await next.Send(context);
        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(context.CancellationToken);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("test-save-changes");
}
