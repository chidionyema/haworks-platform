namespace Haworks.Scheduler.Domain.Entities;

public sealed class VaultLease
{
    public Guid Id { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string RoleName { get; private set; } = string.Empty;
    public string CredentialType { get; private set; } = string.Empty; // "database", "pki", "kv"
    public string? LeaseId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastRotatedAt { get; private set; }
    public VaultLeaseStatus Status { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Postgres xmin concurrency token (matches existing platform pattern).</summary>
    public uint xmin { get; set; }

    private VaultLease() { } // EF constructor

    public static VaultLease Create(string serviceName, string roleName, string credentialType, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialType);

        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");

        return new VaultLease
        {
            Id = Guid.NewGuid(),
            ServiceName = serviceName,
            RoleName = roleName,
            CredentialType = credentialType,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + ttl,
            Status = VaultLeaseStatus.Active
        };
    }

    public void MarkRotating()
    {
        if (Status != VaultLeaseStatus.Active)
            throw new InvalidOperationException($"Cannot transition to Rotating from {Status}. Only Active leases can be rotated.");

        Status = VaultLeaseStatus.Rotating;
    }

    public void MarkRotated(string newLeaseId, DateTimeOffset expiresAt)
    {
        if (Status != VaultLeaseStatus.Rotating)
            throw new InvalidOperationException($"Cannot transition to Active (rotated) from {Status}. Only Rotating leases can be marked rotated.");

        ArgumentException.ThrowIfNullOrWhiteSpace(newLeaseId);

        LeaseId = newLeaseId;
        ExpiresAt = expiresAt;
        LastRotatedAt = DateTimeOffset.UtcNow;
        LastError = null;
        Status = VaultLeaseStatus.Active;
    }

    public void MarkFailed(string reason)
    {
        LastError = reason;
        Status = VaultLeaseStatus.Failed;
    }

    public void MarkExpired()
    {
        Status = VaultLeaseStatus.Expired;
    }

    /// <summary>
    /// Returns true if the lease has elapsed past the given threshold of its total TTL.
    /// Default threshold is 80%.
    /// </summary>
    public bool NeedsRotation(double thresholdPercent = 0.8)
    {
        if (Status != VaultLeaseStatus.Active)
            return false;

        var totalTtl = ExpiresAt - CreatedAt;
        if (totalTtl <= TimeSpan.Zero)
            return true;

        var elapsed = DateTimeOffset.UtcNow - CreatedAt;
        var ratio = elapsed / totalTtl;
        return ratio >= thresholdPercent;
    }

    /// <summary>
    /// Reset a stale Rotating status (stuck for too long) back to Failed.
    /// </summary>
    public void ResetStaleRotating(string reason)
    {
        if (Status != VaultLeaseStatus.Rotating)
            throw new InvalidOperationException($"Cannot reset from {Status}. Only Rotating leases can be reset.");

        LastError = reason;
        Status = VaultLeaseStatus.Failed;
    }
}

public enum VaultLeaseStatus
{
    Active,
    Rotating,
    Expired,
    Failed
}
