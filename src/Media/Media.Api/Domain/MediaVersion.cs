namespace Haworks.Media.Api.Domain;

public sealed class MediaVersion
{
    public Guid Id { get; init; }
    public required Guid MediaFileId { get; init; }
    public required int VersionNumber { get; init; }
    public required string ObjectName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public static MediaVersion Create(Guid mediaFileId, int versionNumber, string objectName) =>
        new() { Id = Guid.NewGuid(), MediaFileId = mediaFileId, VersionNumber = versionNumber, ObjectName = objectName };
}
