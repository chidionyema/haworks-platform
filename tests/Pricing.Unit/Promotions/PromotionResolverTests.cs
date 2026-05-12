using FluentAssertions;
using Haworks.Pricing.Application.Commands;
using Haworks.Pricing.Application.Promotions;
using Haworks.Pricing.Domain.Aggregates;
using Haworks.Pricing.Domain.Enums;
using Haworks.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Pricing.Unit.Promotions;

public sealed class PromotionResolverTests
{
    private readonly Mock<IPromotionRepository> _repo = new();
    private readonly PromotionResolver _resolver;

    public PromotionResolverTests()
    {
        _resolver = new PromotionResolver(_repo.Object);
    }

    [Fact]
    public async Task ResolveApplicablePromotionsAsync_filters_by_MinOrderValue()
    {
        var now = DateTime.UtcNow;
        var promo = Promotion.Create("P1", "D1", DiscountType.PercentOff, 10, now.AddDays(-1), now.AddDays(1));
        promo.AddRule(RuleType.MinimumOrderValue, "100");
        
        _repo.Setup(r => r.GetActivePromotionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { promo });

        var r1 = new GetPriceQuoteCommand(new[] { new CartLineDto(Guid.NewGuid(), 1, 50) });
        (await _resolver.ResolveApplicablePromotionsAsync(r1)).Should().BeEmpty();

        var r2 = new GetPriceQuoteCommand(new[] { new CartLineDto(Guid.NewGuid(), 1, 150) });
        (await _resolver.ResolveApplicablePromotionsAsync(r2)).Should().HaveCount(1);
    }

    [Fact]
    public async Task ResolveApplicablePromotionsAsync_filters_by_SpecificProduct()
    {
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();
        var promo = Promotion.Create("P1", "D1", DiscountType.PercentOff, 10, now.AddDays(-1), now.AddDays(1));
        promo.AddRule(RuleType.SpecificProduct, productId.ToString());
        
        _repo.Setup(r => r.GetActivePromotionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { promo });

        var r1 = new GetPriceQuoteCommand(new[] { new CartLineDto(Guid.NewGuid(), 1, 100) });
        (await _resolver.ResolveApplicablePromotionsAsync(r1)).Should().BeEmpty();

        var r2 = new GetPriceQuoteCommand(new[] { new CartLineDto(productId, 1, 100) });
        (await _resolver.ResolveApplicablePromotionsAsync(r2)).Should().HaveCount(1);
    }
}
