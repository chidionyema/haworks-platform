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

        if (msg.PaymentId == Guid.Empty || msg.AmountCents <= 0)
        {
            logger.LogDebug(
                "Skipping payment compensation for RefundCancelled {RefundId} — no PaymentId/AmountCents (secondary notification)",
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

        if (payment.TotalRefundedCents < msg.AmountCents)
        {
            logger.LogWarning(
                "Payment {PaymentId} TotalRefundedCents ({TotalRefundedCents}) is less than reversal amount ({AmountCents}). RefundId={RefundId}. Skipping to avoid negative balance.",
                msg.PaymentId, payment.TotalRefundedCents, msg.AmountCents, msg.RefundId);
            return;
        }

        payment.ReverseRefund(msg.AmountCents);

        logger.LogInformation(
            "Reversed refund of {AmountCents} cents on Payment {PaymentId}. New TotalRefundedCents={TotalRefundedCents}. RefundId={RefundId}",
            msg.AmountCents, msg.PaymentId, payment.TotalRefundedCents, msg.RefundId);
    }
}
