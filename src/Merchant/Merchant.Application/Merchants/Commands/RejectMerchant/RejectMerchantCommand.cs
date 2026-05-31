using FluentValidation;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Common.Interfaces;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Merchant.Application.Merchants.Commands.RejectMerchant;

public record RejectMerchantCommand(Guid MerchantId, string RejectedBy, string Reason, string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result>;

public class RejectMerchantCommandValidator : AbstractValidator<RejectMerchantCommand>
{
    public RejectMerchantCommandValidator()
    {
        RuleFor(v => v.MerchantId).NotEmpty();
        RuleFor(v => v.RejectedBy).NotEmpty();
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectMerchantCommandHandler : IRequestHandler<RejectMerchantCommand, Result>
{
    private readonly IMerchantDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public RejectMerchantCommandHandler(IMerchantDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Result> Handle(RejectMerchantCommand request, CancellationToken cancellationToken)
    {
        var merchant = await _context.Merchants
            .FirstOrDefaultAsync(m => m.Id == request.MerchantId, cancellationToken);

        if (merchant is null)
            return Result.Failure(Error.NotFound("Merchant.NotFound", "Merchant not found."));

        try
        {
            merchant.Reject(request.RejectedBy, request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Validation("Merchant.InvalidStatusTransition", ex.Message));
        }

        await _publishEndpoint.Publish(new MerchantRejectedEvent
        {
            MerchantId = merchant.Id,
            RejectedBy = request.RejectedBy,
            Reason = request.Reason
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
