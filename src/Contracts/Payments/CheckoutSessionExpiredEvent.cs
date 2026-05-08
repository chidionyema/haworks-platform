namespace Haworks.Contracts.Payments;

/// <summary>
/// Published when a checkout session expires before completion.
/// </summary>
public sealed record CheckoutSessionExpiredEvent : DomainEvent
{
    public required string SessionId { get; init; }
    
    /// <summary>
    /// For microservices, we must provide the opaque correlation IDs if known.
    /// </summary>
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    
    public required string Provider { get; init; }
}
