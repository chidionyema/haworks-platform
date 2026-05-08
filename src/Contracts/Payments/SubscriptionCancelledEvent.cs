namespace Haworks.Contracts.Payments;

public sealed record SubscriptionCancelledEvent : DomainEvent
{
    public required string SubscriptionId { get; init; }
    public required string UserId { get; init; }
    public required PaymentProvider Provider { get; init; }
    public string? Reason { get; init; }
    public DateTime CancelledAt { get; init; } = DateTime.UtcNow;
}
