using Haworks.BuildingBlocks.Idempotency;
using Haworks.BuildingBlocks.Persistence;
using Haworks.BuildingBlocks.Messaging;
using Haworks.BuildingBlocks.Vault;
using Haworks.Location.Application.Interfaces;
using Haworks.Location.Infrastructure.Persistence;
using Haworks.Location.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Haworks.Location.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        var connectionString = configuration.GetConnectionString("location")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:location is missing. Aspire injects it via WithReference(locationDb).");

        var vaultEnabled = configuration.GetValue("Vault:Enabled", false)
            && !env.IsEnvironment("Test");
            
        if (vaultEnabled)
        {
            services.AddVaultIntegration(configuration);
            services.AddVaultNpgsqlDataSource(connectionString, "haworks-location");
        }

        services.AddDbContext<LocationDbContext>((sp, options) =>
        {
            if (vaultEnabled)
            {
                options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "location");
                    // Enable NetTopologySuite for PostGIS support
                    npgsql.UseNetTopologySuite();
                });
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "location");
                    // Enable NetTopologySuite for PostGIS support
                    npgsql.UseNetTopologySuite();
                });
            }
            options.AddPlatformInterceptors(sp);
        });

        services.AddScoped<ILocationDbContext>(sp => sp.GetRequiredService<LocationDbContext>());
        services.AddScoped<IIdempotencyJournalDbContext>(sp => sp.GetRequiredService<LocationDbContext>());

        // Geospatial services
        services.AddSingleton<IGeohashService, GeohashService>();

        services.AddHttpClient<IGeocodingService, NominatimGeocodingService>((sp, c) =>
        {
            var nominatimUrl = configuration["Location:NominatimBaseUrl"] ?? "https://nominatim.openstreetmap.org/";
            if (!Uri.TryCreate(nominatimUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                IsPrivateNetwork(uri.Host))
            {
                throw new InvalidOperationException($"Invalid Nominatim URL: {nominatimUrl}");
            }
            c.BaseAddress = new Uri(nominatimUrl);
            c.DefaultRequestHeaders.Add("User-Agent", "HaworksPlatform/1.0");
            var t = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Haworks.BuildingBlocks.Resilience.HttpClientTimeoutOptions>>().Value;
            c.Timeout = TimeSpan.FromSeconds(t.LocationNominatimSeconds);
        })
        .AddStandardResilienceHandler();

        if (env.IsEnvironment("Test"))
        {
            return services;
        }

        services.AddMassTransitDiagnostics();

        services.AddMassTransit(mt =>
        {
            mt.SetKebabCaseEndpointNameFormatter();

            mt.AddConsumer<GlobalFaultConsumer>();
            mt.AddEntityFrameworkOutbox<LocationDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });

            mt.UsingRabbitMq((context, cfg) =>
            {
                cfg.ConfigureStandardHost(configuration);
                cfg.ConfigureStandardRabbitMq(context);
            });
        });

        return services;
    }

    private static bool IsPrivateNetwork(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            return ip.IsIPv4MappedToIPv6 ? IsPrivateIPv4(bytes[12..]) :
                   ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? IsPrivateIPv4(bytes) :
                   false; // Don't block IPv6 for simplicity
        }
        return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes[0] == 10 ||
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
        (bytes[0] == 192 && bytes[1] == 168) ||
        (bytes[0] == 127);
}
