namespace Haworks.Content.Domain.Entities;

/// <summary>
/// Lifecycle of a <see cref="ContentEntity"/> from upload-initiated to
/// finalised (or terminated). Transitions are one-way: Pending →
/// Validating → (Available | Quarantined | Failed); Available → Deleted.
/// </summary>
public enum ContentStatus
{
    /// <summary>
    /// Upload has been initiated; the client has been issued presigned
    /// URLs but no validated bytes are confirmed yet. Sweeper expires
    /// rows stuck here past the configured TTL.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Client has signalled completion; server is running magic-byte +
    /// virus + checksum validation against the object now in S3.
    /// </summary>
    Validating = 1,

    /// <summary>
    /// Validation succeeded; the content is publishable and reachable
    /// via presigned GET / CDN URL.
    /// </summary>
    Available = 2,

    /// <summary>
    /// Validation failed (virus / signature mismatch). The S3 object has
    /// been moved to a quarantine prefix; the row is retained for audit.
    /// </summary>
    Quarantined = 3,

    /// <summary>
    /// A non-recoverable error occurred during finalisation (e.g. S3
    /// CompleteMultipartUpload failure, checksum mismatch on a parted
    /// upload). Distinct from <see cref="Quarantined"/>: failures are
    /// retryable in principle; quarantines are not.
    /// </summary>
    Failed = 4,

    /// <summary>Soft-deleted. Bytes may still be in S3 pending GC.</summary>
    Deleted = 5,
}
