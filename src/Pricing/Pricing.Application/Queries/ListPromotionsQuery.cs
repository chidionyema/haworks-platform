using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Queries;

public sealed record ListPromotionsQuery(int Skip = 0, int Take = 20) : IRequest<Result<PagedResult<PromotionDto>>>;

public sealed record PromotionDto(
    Guid Id,
    string Name,
    string Description,
    string Code,
    DateTime StartDate,
    DateTime? EndDate,
    bool IsActive);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);
