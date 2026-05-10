using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Commands;

public sealed record DeletePromotionCommand(Guid Id) : IRequest<Result>;
