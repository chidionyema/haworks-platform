using MassTransit;
using Haworks.Contracts.Payments;
using Haworks.Payments.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Application.Consumers;

/// <summary>
/// Compensates Payment.TotalRefunded when a refund is cancelled.
/// The RefundSaga publishes RefundCancelledEvent with PaymentId + Amount
/// populated from saga state. This consumer reverses the deduction so
/// subsequent refund attempts are not blocked by an inflated TotalRefunded.
/// </summary>
public sealed class RefundCancelledPaymentCompensationConsumer(
    IPaymentRepository paymentRepository,
    ILogger<RefundCancelledPaymentCompensationConsumer> logger)
    : IConsumer<RefundCancelledEvent>
{
    public async Task Consume(ConsumeContext<RefundCancelledEvent> context)
    {
        var msg = context.Message;

        if (msg.PaymentId == Guid.Empty || msg.Amount <= 0)
        {
            logger.LogDebug(
                "Skipping payment compensation for RefundCancelled {RefundId} — no PaymentId/Amount (secondary notification)",
                msg.RefundId);
            return;
        }

        var payment = await paymentRepository.GetByIdTrackedAsync(msg.PaymentId, context.CancellationToken);
        if (payment is null)
        {
            logger.LogWarning("Payment {PaymentId} not found for refund cancellation compensation. RefundId={RefundId}",
                msg.PaymentId, msg.RefundId);
            return;
        }

        if (payment.TotalRefunded < msg.Amount)
        {
            logger.LogWarning(
                "Payment {PaymentId} TotalRefunded ({TotalRefunded}) is less than reversal amount ({Amount}). RefundId={RefundId}. Skipping to avoid negative balance.",
                msg.PaymentId, payment.TotalRefunded, msg.Amount, msg.RefundId);
            return;
        }

        payment.ReverseRefund(msg.Amount);

        logger.LogInformation(
            "Reversed refund of {Amount} on Payment {PaymentId}. New TotalRefunded={TotalRefunded}. RefundId={RefundId}",
            msg.Amount, msg.PaymentId, payment.TotalRefunded, msg.RefundId);
    }
}
