using Haworks.Payments.Application.Interfaces;
using Haworks.BuildingBlocks.Common;
using Haworks.Contracts.Payments;
using MediatR;

namespace Haworks.Payments.Application.Commands.Refunds;

public sealed record CreateRefundCommand(
    string TransactionId, 
    long? AmountCents = null, 
    string? Currency = null, 
    string? Reason = null, 
    string? IdempotencyKey = null) : IRequest<Result<RefundResult>>;

public sealed class CreateRefundCommandHandler(
    IRefundService refundService) : IRequestHandler<CreateRefundCommand, Result<RefundResult>>
{
    public async Task<Result<RefundResult>> Handle(CreateRefundCommand request, CancellationToken ct)
    {
        var refundRequest = new RefundRequest
        {
            TransactionId = request.TransactionId,
            AmountCents = request.AmountCents,
            Currency = request.Currency,
            Reason = request.Reason,
            IdempotencyKey = request.IdempotencyKey
        };

        var result = await refundService.CreateRefundAsync(refundRequest, ct);
        
        return result.Status == RefundStatus.Failed 
            ? Result<RefundResult>.Failure<RefundResult>(Error.Validation("Refund.Failed", result.FailureReason ?? "Refund creation failed."))
            : Result<RefundResult>.Success(result);
    }
}
