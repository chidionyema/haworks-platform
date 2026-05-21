namespace Haworks.Contracts.Orders;

public sealed record OrderStatusChanged : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string NewStatus { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}
