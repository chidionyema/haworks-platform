namespace Haworks.Contracts.Payments;

/// <summary>
/// Published when a payment is successfully completed.
/// Consumers can use this to:
/// - Update order status to paid
/// - Send payment confirmation emails
/// - Trigger shipping/fulfillment
/// - Update financial records
/// </summary>
public sealed record PaymentCompletedEvent : DomainEvent
{
    /// <summary>The unique identifier of the payment.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The order this payment is for.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>The saga correlation ID for distributed tracing.</summary>
    public required Guid SagaId { get; init; }

    /// <summary>The amount paid in minor currency units (cents).</summary>
    public required long AmountCents { get; init; }

    /// <summary>Backward-compat for consumers not yet migrated to AmountCents.</summary>
    [Obsolete("Use AmountCents. Will be removed in next major version.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Info Code Smell", "S1133", Justification = "Intentional deprecation for backward compatibility")]
    public decimal Amount => Math.Round(AmountCents / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>The currency code (e.g., "USD", "EUR").</summary>
    public required string Currency { get; init; }

    /// <summary>The payment provider used (e.g., "Stripe", "PayPal").</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's transaction reference.</summary>
    public string? TransactionReference { get; init; }

    /// <summary>The seller who should be credited for this payment (used by Payouts).</summary>
    public Guid SellerId { get; init; }
}
