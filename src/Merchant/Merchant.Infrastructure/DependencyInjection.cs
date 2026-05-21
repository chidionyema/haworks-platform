using Haworks.BuildingBlocks.Persistence;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Infrastructure.Persistence;
using Haworks.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Haworks.Merchant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var connectionString = configuration.GetConnectionString("merchant");

        services.AddDbContext<MerchantDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddPlatformInterceptors(sp);
        });

        services.AddScoped<IMerchantDbContext>(provider => provider.GetRequiredService<MerchantDbContext>());

        if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Test", StringComparison.Ordinal))
        {
            services.AddMassTransitDiagnostics();

        services.AddMassTransit(x =>
            {
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<GlobalFaultConsumer>();
                x.AddEntityFrameworkOutbox<MerchantDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.ConfigureStandardHost(configuration);
                    cfg.ConfigureStandardRabbitMq(context);
                });
            });
        }

        return services;
    }
}
