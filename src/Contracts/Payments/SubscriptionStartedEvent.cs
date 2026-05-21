namespace Haworks.Contracts.Payments;

public sealed record SubscriptionStartedEvent : DomainEvent
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptionId")]
    public required string ProviderSubscriptionId { get; init; }
    public required string UserId { get; init; }
    public required string PlanId { get; init; }
    public required PaymentProvider Provider { get; init; }
    public DateTime CurrentPeriodEnd { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
}
