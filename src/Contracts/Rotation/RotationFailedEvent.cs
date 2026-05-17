namespace Haworks.Contracts.Rotation;

/// <summary>
/// Published after a rotation attempt fails (after exhausting retries).
/// </summary>
public sealed record RotationFailedEvent : DomainEvent
{
    public required string ServiceName { get; init; }
    public required string RoleName { get; init; }
    public required string Reason { get; init; }
    public required int AttemptCount { get; init; }
}
