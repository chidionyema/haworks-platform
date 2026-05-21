using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PostHog;
using PostHog.Config;

namespace Haworks.BuildingBlocks.PostHog;

public static class PostHogExtensions
{
    /// <summary>
    /// Registers the PostHog analytics client.
    /// Reads configuration from the "PostHog" section:
    ///   PostHog:ProjectToken — project API key (required)
    ///   PostHog:HostUrl      — self-hosted URL, e.g. http://posthog:8000 (required for self-hosted)
    /// </summary>
    public static IServiceCollection AddHaworksPostHog(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("PostHog");

        services.AddPostHog(builder => builder.UseConfigurationSection(section));

        return services;
    }
}
