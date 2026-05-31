using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Common.Interfaces;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Merchant.Application.Merchants.Commands.ApproveMerchant;

public record ApproveMerchantCommand(Guid MerchantId, string ApprovedBy, string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result>;

public sealed class ApproveMerchantCommandHandler : IRequestHandler<ApproveMerchantCommand, Result>
{
    private readonly IMerchantDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public ApproveMerchantCommandHandler(IMerchantDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Result> Handle(ApproveMerchantCommand request, CancellationToken cancellationToken)
    {
        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.Id == request.MerchantId, cancellationToken);

        if (merchant is null)
            return Result.Failure(Error.NotFound("Merchant.NotFound", "Merchant not found."));

        try
        {
            merchant.Activate(request.ApprovedBy);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Validation("Merchant.InvalidStatusTransition", ex.Message));
        }

        await _publishEndpoint.Publish(new MerchantActivatedEvent
        {
            MerchantId = merchant.Id
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
