namespace Haworks.Scheduler.Domain.Entities;

public sealed class RotationAuditEntry
{
    public Guid Id { get; private set; }
    public Guid LeaseId { get; private set; }
    public string Action { get; private set; } = string.Empty; // "rotate", "revoke", "renew", "fail"
    public DateTimeOffset Timestamp { get; private set; }
    public string? NewLeaseId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool Success { get; private set; }

    private RotationAuditEntry() { } // EF constructor

    public static RotationAuditEntry Record(
        Guid leaseId,
        string action,
        bool success,
        string? newLeaseId = null,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new RotationAuditEntry
        {
            Id = Guid.NewGuid(),
            LeaseId = leaseId,
            Action = action,
            Timestamp = DateTimeOffset.UtcNow,
            NewLeaseId = newLeaseId,
            ErrorMessage = error,
            Success = success
        };
    }
}
