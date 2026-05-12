using Haworks.BuildingBlocks.Common;
using MediatR;

namespace Haworks.Pricing.Application.Commands;

internal sealed class DeletePromotionCommandHandler : IRequestHandler<DeletePromotionCommand, Result>
{
    public Task<Result> Handle(DeletePromotionCommand request, CancellationToken ct)
    {
        return Task.FromResult(Result.Success());
    }
}
