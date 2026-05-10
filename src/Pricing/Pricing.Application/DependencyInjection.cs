using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Pricing.Application;

/// <summary>
/// Top-level DI orchestrator. Calls per-track stubs in DependencyInjection.&lt;TrackId&gt;.cs.
/// Written ONCE at L0 by 'wave run'; not modified by L1 tracks.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPricingApplication(this IServiceCollection services) => services
        .AddPricingT1()
        .AddPricingT2()
        .AddPricingT3()
        .AddPricingT4();
}
