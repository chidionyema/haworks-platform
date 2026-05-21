namespace Haworks.Contracts.Payments;

public sealed record SubscriptionRenewedEvent : DomainEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptionId")]
    public required string ProviderSubscriptionId { get; init; }
    public required string UserId { get; init; }
    public required PaymentProvider Provider { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public DateTime NewPeriodEnd { get; init; }
    public DateTime RenewedAt { get; init; } = DateTime.UtcNow;
}
