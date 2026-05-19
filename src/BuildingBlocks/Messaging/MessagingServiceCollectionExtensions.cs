using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Standardizes the RabbitMQ bus configuration with a baseline retry policy
    /// (3 attempts, incremental backoff) that applies to all endpoints.
    /// </summary>
    public static void ConfigureStandardRabbitMq(
        this IRabbitMqBusFactoryConfigurator cfg,
        IBusRegistrationContext context)
    {
        // Stage 1: Immediate retries (transient blips — network glitch, brief DB lock)
        cfg.UseMessageRetry(r => r.Incremental(
            retryLimit: 3,
            initialInterval: TimeSpan.FromSeconds(1),
            intervalIncrement: TimeSpan.FromSeconds(2)));

        // Stage 2: Delayed redelivery (service outages — Stripe down, DB failover)
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30)));

        // Platform-wide observability: log every consumer fault and dead-letter
        cfg.ConnectConsumeObserver(context.GetRequiredService<DiagnosticConsumeObserver>());
        cfg.ConnectReceiveObserver(context.GetRequiredService<DiagnosticReceiveObserver>());

        cfg.ConfigureEndpoints(context);
    }

    /// <summary>
    /// Registers the diagnostic observers in DI. Call from each service's
    /// MassTransit setup (or from AddServiceDefaults).
    /// </summary>
    public static IServiceCollection AddMassTransitDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<DiagnosticConsumeObserver>();
        services.AddSingleton<DiagnosticReceiveObserver>();
        return services;
    }
}
