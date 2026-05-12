using Haworks.BuildingBlocks.Resilience;
using Haworks.Search.Application.Catalog;
using Haworks.Search.Application.Consumers;
using Haworks.Search.Application.Interfaces;
using Haworks.Search.Infrastructure.Catalog;
using Haworks.Search.Infrastructure.Meilisearch;
using Haworks.Search.Infrastructure.Options;
using MassTransit;
using Meilisearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Haworks.Search.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        services.AddOptions<MeilisearchOptions>()
            .Bind(configuration.GetSection(MeilisearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<MeilisearchClient>(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            return new MeilisearchClient(opt.Url, opt.MasterKey);
        });

        services.AddScoped<ISearchIndex, MeilisearchIndex>();

        // Catalog HTTP client. The resilience policy is constructed inside
        // CatalogProductsApiClient and wraps each call — matching the
        // Stripe/PayPal pattern — so AddPolicyHandler isn't needed here.
        services.AddSingleton<IResiliencePolicyFactory, ResiliencePolicyFactory>();
        services.AddHttpClient<ICatalogProductsApi, CatalogProductsApiClient>(c =>
        {
            c.BaseAddress = new Uri(configuration["Catalog:BaseAddress"]
                ?? "http://ritualworks-catalog.flycast:8080");
            c.Timeout = TimeSpan.FromSeconds(5);
        });

        // MassTransit + RabbitMQ. Skipped under Test — SearchWebAppFactory
        // wires AddMassTransitTestHarness with the same consumers.
        if (!env.IsEnvironment("Test"))
        {
            services.AddMassTransit(mt =>
            {
                mt.SetKebabCaseEndpointNameFormatter();
                mt.AddConsumer<ProductCacheInvalidatedConsumer>();
                mt.AddConsumer<CategoryUpdatedConsumer>();
                mt.AddConsumer<LocationUpdatedConsumer>();

                mt.UsingRabbitMq((ctx, cfg) =>
                {
                    var rabbit = configuration.GetConnectionString("rabbitmq")
                        ?? throw new InvalidOperationException("ConnectionStrings:rabbitmq is missing");
                    cfg.Host(new Uri(rabbit));
                    cfg.ConfigureEndpoints(ctx);
                });
            });
        }

        return services;
    }
}
