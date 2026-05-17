namespace Haworks.Contracts.Media;

/// <summary>
/// Fired when a media file passes validation and is marked Available.
/// Consumers: Search (index), Notifications (notify owner), Catalog (link).
/// </summary>
public sealed record MediaAvailableEvent : DomainEvent
{
    public required Guid MediaId { get; init; }
    public Guid? EntityId { get; init; }
    public string? EntityType { get; init; }
    public required Guid OwnerId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long Size { get; init; }
    public string? Slug { get; init; }
}
