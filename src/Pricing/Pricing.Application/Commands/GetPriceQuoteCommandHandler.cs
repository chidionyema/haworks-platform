using Haworks.BuildingBlocks.Common;
using Haworks.Pricing.Application.Promotions;
using MediatR;

namespace Haworks.Pricing.Application.Commands;

internal sealed class GetPriceQuoteCommandHandler : IRequestHandler<GetPriceQuoteCommand, Result<PriceQuoteDto>>
{
    private readonly IPromotionResolver _resolver;
    private readonly IDiscountCalculator _calculator;

    public GetPriceQuoteCommandHandler(IPromotionResolver resolver, IDiscountCalculator calculator)
    {
        _resolver = resolver;
        _calculator = calculator;
    }
    
    public async Task<Result<PriceQuoteDto>> Handle(GetPriceQuoteCommand request, CancellationToken ct)
    {
        var applicablePromotions = await _resolver.ResolveApplicablePromotionsAsync(request, ct);
        
        var quote = _calculator.CalculateQuote(request, applicablePromotions);

        return Result.Success(quote);
    }
}
