namespace Haworks.Contracts.Payments;

public sealed record SubscriptionRenewedEvent : DomainEvent
{
    public required string SubscriptionId { get; init; }
    public required string UserId { get; init; }
    public required PaymentProvider Provider { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public DateTime NewPeriodEnd { get; init; }
    public DateTime RenewedAt { get; init; } = DateTime.UtcNow;
}
