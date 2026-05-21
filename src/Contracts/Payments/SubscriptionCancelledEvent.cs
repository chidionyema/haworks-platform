namespace Haworks.Contracts.Payments;

public sealed record SubscriptionCancelledEvent : DomainEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptionId")]
    public required string ProviderSubscriptionId { get; init; }
    public required string UserId { get; init; }
    public required PaymentProvider Provider { get; init; }
    public string? Reason { get; init; }
    public DateTime CancelledAt { get; init; } = DateTime.UtcNow;
}
