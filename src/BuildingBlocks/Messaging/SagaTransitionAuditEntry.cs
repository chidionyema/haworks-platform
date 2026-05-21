namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// Append-only audit trail for saga state transitions.
/// Written in the same EF transaction as the saga state change
/// via SagaPersistenceInterceptor.
/// </summary>
public sealed class SagaTransitionAuditEntry
{
    public long Id { get; init; }
    public required string SagaType { get; init; }
    public required Guid CorrelationId { get; init; }
    public required string FromState { get; init; }
    public required string ToState { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UserId or service identity that triggered the transition, extracted from
    /// the MassTransit message header "UserId". Null when no header is present
    /// (e.g. scheduled events, internal saga-to-saga transitions).
    /// </summary>
    public string? InitiatedBy { get; init; }
}
