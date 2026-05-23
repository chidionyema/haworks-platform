namespace Haworks.Contracts.Payments;

public sealed record RefundCancelledEvent : DomainEvent
{
    public required Guid RefundId { get; init; }
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
    public Guid PaymentId { get; init; }
<<<<<<< HEAD
    public long AmountCents { get; init; }
=======
    public decimal Amount { get; init; }
>>>>>>> origin/main

    /// <summary>ISO 4217 currency code (e.g., "USD", "EUR").</summary>
    public required string Currency { get; init; }
}
