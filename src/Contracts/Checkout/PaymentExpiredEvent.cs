namespace Haworks.Contracts.Checkout;

/// <summary>
/// Saga-internal tick fired by the MassTransit message scheduler when
/// a checkout saga has been sitting in StockReserved or ReadyForPayment
/// past its payment-expiry deadline (15 min by default). The saga
/// catches it and runs the same compensation as a PaymentSessionFailed:
/// publish StockReleaseRequested + transition to Abandoned.
///
/// Closes the gap where a customer abandons the Stripe/PayPal session
/// after stock has been reserved -- without this timer the reservation
/// would be locked indefinitely.
/// </summary>
public sealed record PaymentExpiredEvent : DomainEvent
{
    public required Guid SagaId { get; init; }
    public required Guid OrderId { get; init; }
}
