namespace Haworks.Contracts.Payments;

public sealed record RefundIssuedEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string RefundId { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required PaymentProvider Provider { get; init; }
    public string? Reason { get; init; }
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
}
