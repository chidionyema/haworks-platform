namespace Haworks.Contracts.Rotation;

/// <summary>
/// Published by LeaseWatcherJob after a successful Postgres credential rotation.
/// </summary>
public sealed record CredentialRotatedEvent : DomainEvent
{
    public required string ServiceName { get; init; }
    public required string RoleName { get; init; }
    public required string LeaseId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
