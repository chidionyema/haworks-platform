using Haworks.BuildingBlocks.Persistence;
using Haworks.Webhooks.Application.Interfaces;
using Haworks.Webhooks.Infrastructure.Persistence;
using Haworks.Webhooks.Infrastructure.Svix;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Svix;

namespace Haworks.Webhooks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWebhooksInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        var connectionString = configuration.GetConnectionString("webhooks");

        services.AddDbContext<WebhooksDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "webhooks");
            });
            options.AddPlatformInterceptors(sp);
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<IWebhooksDbContext>(sp => sp.GetRequiredService<WebhooksDbContext>());

        // Svix — all webhook dispatch, retry, signing, SSRF protection delegated to Svix server.
        // Both Svix:ServerUrl and Svix:AuthToken MUST be provided via configuration (env vars, Vault, etc.).
        var svixServerUrl = configuration["Svix:ServerUrl"]
            ?? throw new InvalidOperationException("Svix:ServerUrl is required. Set via configuration or environment variable Svix__ServerUrl.");
        var svixAuthToken = configuration["Svix:AuthToken"] ?? "";
        services.AddSingleton(new SvixClient(svixAuthToken, new SvixOptions(svixServerUrl)));
        services.AddScoped<IWebhookDispatcher, SvixWebhookForwarder>();

        // HttpClient for subscription URL validation (kept for SubscriptionHandlers)
        services.AddHttpClient("WebhookValidator")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            })
            .ConfigureHttpClient((sp, c) =>
            {
                var t = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Haworks.BuildingBlocks.Resilience.HttpClientTimeoutOptions>>().Value;
                c.Timeout = TimeSpan.FromSeconds(t.WebhooksDispatchSeconds);
            });

        return services;
    }
}
