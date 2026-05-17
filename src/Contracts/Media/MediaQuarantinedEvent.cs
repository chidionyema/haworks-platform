namespace Haworks.Contracts.Media;

/// <summary>
/// Fired when a media file fails virus/signature validation and is quarantined.
/// Consumers: Notifications (alert owner), Audit (record quarantine).
/// </summary>
public sealed record MediaQuarantinedEvent : DomainEvent
{
    public required Guid MediaId { get; init; }
    public Guid? EntityId { get; init; }
    public string? EntityType { get; init; }
    public required Guid OwnerId { get; init; }
    public required string Reason { get; init; }
    public required string FileName { get; init; }
}
