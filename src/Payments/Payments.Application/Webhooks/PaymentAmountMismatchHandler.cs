using System.Globalization;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.Contracts.Payments;
using MassTransit;
using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain;
using Haworks.Payments.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Haworks.Payments.Application.Webhooks;

/// <summary>
/// Handles payment amount mismatches in a consistent way across providers.
/// Flags the payment for review and publishes an event for cross-context updates.
/// </summary>
public sealed class PaymentAmountMismatchHandler : IPaymentAmountMismatchHandler
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPublishEndpoint _eventPublisher;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<PaymentAmountMismatchHandler> _logger;

    public PaymentAmountMismatchHandler(
        IPaymentRepository paymentRepository,
        IPublishEndpoint eventPublisher,
        ITelemetryService telemetry,
        ILogger<PaymentAmountMismatchHandler> logger)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleMismatchAsync(
        Payment payment,
        long actualPaidCents,
        long expectedTotalCents,
        PaymentProvider provider,
        CancellationToken ct = default)
    {
        var differenceCents = Math.Abs(actualPaidCents - expectedTotalCents);
        var reason = $"{provider} paid {new Money(actualPaidCents, payment.Currency)}, " +
            $"Expected {new Money(expectedTotalCents, payment.Currency)}, " +
            $"Diff {new Money(differenceCents, payment.Currency)}";

        _logger.LogCritical(
            "AMOUNT MISMATCH for Payment {PaymentId}: {Reason}",
            payment.Id,
            reason);

        // Flag payment in the Payments context — outbox handles this:
        // SaveChangesAsync + PublishAsync are atomic via the MassTransit outbox.
        payment.Flag();

        // Publish event for Orders context to handle order status update.
        await _eventPublisher.Publish(new PaymentAmountMismatchEvent
        {
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            SagaId = payment.SagaId,
            Provider = provider.ToString(),
            ActualPaidCents = actualPaidCents,
            ExpectedTotalCents = expectedTotalCents,
            DifferenceCents = differenceCents,
            Reason = reason,
            Currency = payment.Currency
        }, ct);

        await _paymentRepository.SaveChangesAsync(ct);

        _telemetry.TrackEvent("PaymentAmountMismatch", new Dictionary<string, string>
        {
            ["Provider"] = provider.ToString(),
            ["OrderId"] = payment.OrderId.ToString(),
            ["PaymentId"] = payment.Id.ToString(),
            ["ActualPaidCents"] = actualPaidCents.ToString(CultureInfo.InvariantCulture),
            ["ExpectedTotalCents"] = expectedTotalCents.ToString(CultureInfo.InvariantCulture),
            ["DifferenceCents"] = differenceCents.ToString(CultureInfo.InvariantCulture),
            ["Currency"] = payment.Currency
        });
    }
}
