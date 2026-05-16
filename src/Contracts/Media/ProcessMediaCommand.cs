namespace Haworks.Contracts.Media;

/// <summary>
/// MassTransit command that triggers async media processing (transcode, thumbnails, audio normalization).
/// Published after virus scan passes. Consumed by <c>ProcessMediaConsumer</c> in the Media service.
/// </summary>
public sealed record ProcessMediaCommand : DomainEvent
{
    public required Guid MediaId { get; init; }
    public required string OwnerId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required string S3Key { get; init; }
}
