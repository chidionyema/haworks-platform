namespace Haworks.Media.Api.Domain;

public sealed class MediaMetadata
{
    public Guid Id { get; init; }
    public required Guid MediaFileId { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }

    public static MediaMetadata Create(Guid mediaFileId, string key, string value) =>
        new() { Id = Guid.NewGuid(), MediaFileId = mediaFileId, Key = key, Value = value };
}
