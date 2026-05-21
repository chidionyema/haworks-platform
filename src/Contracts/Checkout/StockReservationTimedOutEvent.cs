namespace Haworks.Contracts.Checkout;

/// <summary>
/// Saga-internal tick fired by the MassTransit message scheduler when
/// a checkout saga has been sitting in Initiated past its stock-reservation
/// deadline (5 min by default). The saga catches it and transitions to
/// Abandoned with reason "stock_reservation_timeout". No compensation is
/// needed because stock was never reserved.
/// </summary>
public sealed record StockReservationTimedOutEvent : DomainEvent
{
    public required Guid SagaId { get; init; }
    public required Guid OrderId { get; init; }
}
