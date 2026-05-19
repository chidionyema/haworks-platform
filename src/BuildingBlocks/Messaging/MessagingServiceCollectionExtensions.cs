using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static void ConfigureStandardRabbitMq(
        this IRabbitMqBusFactoryConfigurator cfg,
        IBusRegistrationContext context)
    {
        // Retry: 3 immediate attempts (transient blips)
        cfg.UseMessageRetry(r =>
        {
            r.Incremental(
                retryLimit: 3,
                initialInterval: TimeSpan.FromSeconds(1),
                intervalIncrement: TimeSpan.FromSeconds(2));

            // Wire retry observer so every failed attempt is logged
            r.ConnectRetryObserver(context.GetRequiredService<DiagnosticRetryObserver>());
        });

        // Redelivery: 3 delayed attempts (service outages)
        cfg.UseDelayedRedelivery(r => r.Intervals(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30)));

        // Observability: log consume faults + receive faults
        cfg.ConnectConsumeObserver(context.GetRequiredService<DiagnosticConsumeObserver>());
        cfg.ConnectReceiveObserver(context.GetRequiredService<DiagnosticReceiveObserver>());

        cfg.ConfigureEndpoints(context);
    }

    public static IServiceCollection AddMassTransitDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<DiagnosticConsumeObserver>();
        services.AddSingleton<DiagnosticReceiveObserver>();
        services.AddSingleton<DiagnosticRetryObserver>();
        return services;
    }
}
