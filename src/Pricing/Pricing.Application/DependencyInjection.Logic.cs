using Haworks.Pricing.Application.Promotions;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Pricing.Application;

public static class DependencyInjectionLogic
{
    public static IServiceCollection AddPromotionLogic(this IServiceCollection services)
    {
        services.AddScoped<IPromotionResolver, PromotionResolver>();
        services.AddScoped<IDiscountCalculator, DiscountCalculator>();
        
        return services;
    }
}
