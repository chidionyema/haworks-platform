using Haworks.BuildingBlocks.Persistence;
using Haworks.Localization.Api.Application;
using Haworks.Localization.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using FluentValidation;
using Haworks.BuildingBlocks.Behaviors;
using Haworks.BuildingBlocks.Messaging;

namespace Haworks.Localization.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddLocalizationService(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var connectionString = configuration.GetConnectionString("localization")
            ?? throw new InvalidOperationException("ConnectionStrings:localization is required");

        services.AddDbContext<LocalizationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddPlatformInterceptors(sp);
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(TelemetryBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        if (env.IsDevelopment() || env.IsEnvironment("Test"))
        {
            services.AddScoped<ICdnService, MockCdnService>();
        }

        if (env.IsEnvironment("Test"))
        {
            // In Test, register a no-op MassTransit bus so IPublishEndpoint resolves without RabbitMQ.
            services.AddMassTransitDiagnostics();

        services.AddMassTransit(mt =>
            {
                mt.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            });
        }
        else
        {
            services.AddMassTransitDiagnostics();

        services.AddMassTransit(mt =>
            {
                mt.SetKebabCaseEndpointNameFormatter();

                mt.AddConsumer<GlobalFaultConsumer>();
                mt.AddEntityFrameworkOutbox<LocalizationDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                    o.QueryDelay = TimeSpan.FromSeconds(1);
                    o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
                });

                mt.UsingRabbitMq((context, cfg) =>
                {
                    cfg.ConfigureStandardHost(configuration);
                    cfg.ConfigureStandardRabbitMq(context);
                });
            });
        }

        return services;
    }
}
