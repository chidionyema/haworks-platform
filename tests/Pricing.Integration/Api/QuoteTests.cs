using System.Net.Http.Json;
using FluentAssertions;
using Haworks.Pricing.Application.Commands;
using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Domain.Enums;
using Haworks.Pricing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Pricing.Integration.Api;

public sealed class QuoteTests : IClassFixture<PricingWebAppFactory>
{
    private readonly PricingWebAppFactory _factory;

    public QuoteTests(PricingWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Quote_returns_discounted_price()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
            await db.Database.EnsureCreatedAsync();

            var promo = Promotion.Create("10% Off", "D", DiscountType.PercentOff, 10, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            db.Promotions.Add(promo);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var request = new GetPriceQuoteCommand(new[] { new CartLineDto(Guid.NewGuid(), 1, 100) });
        
        var response = await client.PostAsJsonAsync("/price/quote", request);

        response.EnsureSuccessStatusCode();
        var quote = await response.Content.ReadFromJsonAsync<PriceQuoteDto>();
        
        quote.Should().NotBeNull();
        quote!.TotalDiscount.Should().Be(10);
        quote.FinalPrice.Should().Be(90);
    }
}
