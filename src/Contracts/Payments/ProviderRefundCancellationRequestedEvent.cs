namespace Haworks.Contracts.Payments;

public sealed record ProviderRefundCancellationRequestedEvent : DomainEvent
{
    public required Guid RefundId { get; init; }
    public required string ProviderRefundId { get; init; }

    /// <summary>ISO 4217 currency code (e.g., "USD", "EUR").</summary>
    public string? Currency { get; init; }
}
