using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Commands;

internal sealed class CreatePromotionCommandHandler : IRequestHandler<CreatePromotionCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(CreatePromotionCommand request, CancellationToken ct)
    {
        return Task.FromResult(Result.Success(Guid.NewGuid()));
    }
}
