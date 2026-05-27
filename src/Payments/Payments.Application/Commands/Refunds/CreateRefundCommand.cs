using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain.Interfaces;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Contracts.Payments;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Application.Commands.Refunds;

public sealed record CreateRefundCommand(
    Guid PaymentId,
    long AmountCents,
    string Currency,
    string IdempotencyKey,
    string? Reason = null,
    string? RequestedBy = null) : IIdempotentCommand, IRequest<Result<Guid>>;

public sealed class CreateRefundCommandHandler(
    IPaymentRepository paymentRepository,
    IPublishEndpoint eventPublisher,
    ILogger<CreateRefundCommandHandler> logger) : IRequestHandler<CreateRefundCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateRefundCommand request, CancellationToken ct)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, ct);
        if (payment == null)
        {
            return Result.Failure<Guid>(Error.NotFound("Payment.NotFound", $"Payment {request.PaymentId} not found"));
        }

        if (payment.Status != Domain.PaymentStatus.Completed && payment.Status != Domain.PaymentStatus.Refunded)
        {
            return Result.Failure<Guid>(Error.Validation("Payment.NotCompleted", $"Payment must be completed before refund. Current status: {payment.Status}"));
        }

        if (!string.Equals(request.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Guid>(Error.Validation("Refund.CurrencyMismatch", "Refund currency does not match payment currency"));
        }

        // Derive refundId deterministically from idempotency key so API retries
        // produce the same refund ID (prevents duplicate refunds on network timeouts).
        var refundId = GuidFromIdempotencyKey(request.IdempotencyKey);

        // Mutate domain state first — RecordRefund validates remaining amount
        // and throws if total would exceed payment amount.
        try
        {
            payment.RecordRefund(request.AmountCents);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(Error.Validation("Refund.InvalidState", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(Error.Validation("Refund.InvalidAmount", ex.Message));
        }

        // Publish BEFORE SaveChanges so both the entity mutation and the outbox
        // message commit atomically in the same transaction via MassTransit EF Outbox.
        // The Payment entity carries an xmin concurrency token — EF will throw
        // DbUpdateConcurrencyException if another refund raced us.
        await eventPublisher.Publish(new RefundRequestedEvent
        {
            RefundId = refundId,
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            AmountCents = request.AmountCents,
            Currency = request.Currency,
            Reason = request.Reason,
            RequestedBy = request.RequestedBy ?? "Operator"
        }, ct);

        try
        {
            await paymentRepository.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Concurrent refund detected for Payment {PaymentId}; rejecting", payment.Id);
            return Result.Failure<Guid>(Error.Conflict("Refund.ConcurrencyConflict",
                "Another refund for this payment was processed concurrently. Please retry."));
        }

        logger.LogInformation("Refund {RefundId} requested for Payment {PaymentId}", refundId, payment.Id);

        return Result.Success(refundId);
    }

    private static Guid GuidFromIdempotencyKey(string key)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
