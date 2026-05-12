using Haworks.Pricing.Application.Promotions;
using Haworks.Pricing.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Pricing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPricingInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        return services;
    }
}
