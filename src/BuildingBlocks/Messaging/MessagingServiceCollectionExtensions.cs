using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static void ConfigureStandardRabbitMq(
        this IRabbitMqBusFactoryConfigurator cfg,
        IBusRegistrationContext context)
    {
        // IMPORTANT: No bus-level UseMessageRetry.
        // Every consumer MUST have a ConsumerDefinition that inherits
        // BoundedContextConsumerDefinition (for consumers) or
        // BoundedContextSagaDefinition (for sagas). These provide
        // endpoint-level retry OUTSIDE the outbox scope.
        //
        // Consumers without definitions (BFF bridges, GlobalFaultConsumer,
        // Identity, Search, Realtime) currently have NO retry. This is
        // tracked in docs/backlog/masstransit-deep-cleanup.md as
        // Workstream 5: all consumers must have definitions.

        // Observability: log consume faults + receive faults.
        // Use GetService (not GetRequiredService) — diagnostics are optional
        // so services that don't call AddMassTransitDiagnostics() still work.
        var consumeObserver = context.GetService(typeof(DiagnosticConsumeObserver)) as DiagnosticConsumeObserver;
        var receiveObserver = context.GetService(typeof(DiagnosticReceiveObserver)) as DiagnosticReceiveObserver;
        if (consumeObserver != null) cfg.ConnectConsumeObserver(consumeObserver);
        if (receiveObserver != null) cfg.ConnectReceiveObserver(receiveObserver);
        // Saga state persistence is observed via SagaPersistenceInterceptor on the DbContext

        cfg.ConfigureEndpoints(context);
    }

    public static IServiceCollection AddMassTransitDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<DiagnosticConsumeObserver>();
        services.AddSingleton<DiagnosticReceiveObserver>();
        services.AddSingleton<DiagnosticRetryObserver>();
        services.AddSingleton<SagaPersistenceInterceptor>();
        return services;
    }
}
