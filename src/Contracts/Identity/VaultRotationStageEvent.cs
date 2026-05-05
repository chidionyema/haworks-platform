namespace Haworks.Contracts.Identity;

/// <summary>
/// Published by identity-svc when a Vault credential rotation progresses through its stages.
/// Used by the portfolio site demo to visualize the rotation lifecycle in real-time.
/// Stages: started, credentials-fetched, applied, validated, revoked-old
/// </summary>
public sealed record VaultRotationStageEvent : DomainEvent
{
    public required Guid SessionId { get; init; }
    public required string Stage { get; init; }
    public required int NewVersion { get; init; }
    public required string PreviousVersion { get; init; }
    public required DateTime Timestamp { get; init; }
}
