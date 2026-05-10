using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Queries;

internal sealed class ListPromotionsQueryHandler : IRequestHandler<ListPromotionsQuery, Result<PagedResult<PromotionDto>>>
{
    public Task<Result<PagedResult<PromotionDto>>> Handle(ListPromotionsQuery request, CancellationToken ct)
    {
        var items = new List<PromotionDto>();
        return Task.FromResult(Result.Success(new PagedResult<PromotionDto>(items, 0, request.Skip, request.Take)));
    }
}
