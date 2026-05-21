using Haworks.BuildingBlocks.Persistence;
using Haworks.BuildingBlocks.Messaging;
using Haworks.Scheduler.Application.Common.Interfaces;
using Haworks.Scheduler.Application.Jobs;
using Haworks.Scheduler.Infrastructure.Messaging;
using Haworks.Scheduler.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hangfire;
using Hangfire.PostgreSql;
using System;

namespace Haworks.Scheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var connectionString = configuration.GetConnectionString("scheduler");

        services.AddDbContext<SchedulerDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddPlatformInterceptors(sp);
        });

        services.AddScoped<IEventScheduler, HangfireEventScheduler>();
        services.AddScoped<ILeaseRepository, LeaseRepository>();

        if (!env.IsEnvironment("Test"))
        {
            services.AddMassTransitDiagnostics();

        services.AddMassTransit(x =>
            {
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<GlobalFaultConsumer>();
                x.AddEntityFrameworkOutbox<SchedulerDbContext>(o =>
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

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

            services.AddHangfireServer();
        }

        // Rotation jobs
        services.AddScoped<SecretExpiryWatcherJob>();
        services.AddScoped<RotateJwtKeyJob>();
        services.AddScoped<ClearPreviousJwtKeyJob>();
        services.AddScoped<LeaseWatcherJob>();

        return services;
    }
}
