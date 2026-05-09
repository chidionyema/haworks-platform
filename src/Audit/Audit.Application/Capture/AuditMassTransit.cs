using MassTransit;

namespace Haworks.Audit.Application.Capture;

/// <summary>
/// Static seam between L0's <c>Program.cs</c> (which calls <c>AddMassTransit</c>
/// exactly once) and L1.B (which registers per-event consumers via reflection
/// over <c>Haworks.Contracts</c>).
///
/// L0 ships the empty body so the AddMassTransit call wires up cleanly.
/// L1.B replaces the body to register <c>AuditConsumer&lt;TEvent&gt;</c> for
/// every <c>IDomainEvent</c> in the contracts assembly.
///
/// Why static rather than DI-resolved <see cref="IAuditConsumerRegistry"/>:
/// MassTransit's <c>AddMassTransit</c> callback runs before the service
/// provider is built, so we can't resolve interfaces from the container
/// inside it. A static call site avoids the ASP0000 anti-pattern of
/// calling <c>BuildServiceProvider()</c> mid-config.
/// </summary>
public static class AuditMassTransit
{
    public static void RegisterConsumers(IBusRegistrationConfigurator cfg)
    {
        // L1.B: reflect over Haworks.Contracts, register
        // AuditConsumer<TEvent> for every IDomainEvent here.
    }
}
