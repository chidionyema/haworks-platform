using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Commands;

public sealed record CreatePromotionCommand(
    string Name,
    string Description,
    string Code,
    DateTime StartDate,
    DateTime? EndDate,
    bool IsActive = true) : IRequest<Result<Guid>>;
