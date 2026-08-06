using Haworks.BuildingBlocks.Persistence;
using Haworks.Webhooks.Application.Interfaces;
using Haworks.Webhooks.Application.Subscriptions;
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
        var svixAuthToken = configuration["Svix:AuthToken"]
            ?? throw new InvalidOperationException("Svix:AuthToken is required. Set via configuration or environment variable Svix__AuthToken.");
        services.AddSingleton(new SvixClient(svixAuthToken, new SvixOptions(svixServerUrl)));
        services.AddScoped<IWebhookDispatcher, SvixWebhookForwarder>();

        services.AddHttpClient("SvixHealth")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(3));

        // HttpClient for subscription URL validation (kept for SubscriptionHandlers)
        services.AddHttpClient("WebhookValidator")
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                // Pin the connection to a pre-validated, non-private IP so a DNS answer that
                // changes between validation and connect (rebinding) can't redirect this request.
                ConnectCallback = async (context, ct) =>
                {
                    var host = context.DnsEndPoint.Host;
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
                    var safeAddress = addresses.FirstOrDefault(a => !CreateWebhookSubscriptionValidator.IsPrivateIp(a));
                    if (safeAddress is null)
                    {
                        throw new InvalidOperationException($"Webhook host '{host}' resolved to a disallowed address.");
                    }

                    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(safeAddress, context.DnsEndPoint.Port, ct);
                        return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            })
            .ConfigureHttpClient((sp, c) =>
            {
                var t = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Haworks.BuildingBlocks.Resilience.HttpClientTimeoutOptions>>().Value;
                c.Timeout = TimeSpan.FromSeconds(t.WebhooksDispatchSeconds);
            });

        return services;
    }
}
