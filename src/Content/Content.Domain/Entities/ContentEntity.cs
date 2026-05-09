using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Content.Domain.Entities;

public class ContentEntity : AuditableEntity
{
    private readonly List<ContentMetadata> _metadata = [];
    private readonly List<ContentVersion> _versions = [];

    /// <summary>Protected parameterless constructor for EF Core materialization.</summary>
    protected ContentEntity() : base() { }

    private ContentEntity(
        Guid id,
        Guid entityId,
        string entityType,
        ContentType contentType,
        string ownerUserId,
        string fileName,
        string contentTypeMime,
        long expectedSize,
        UploadKind uploadKind,
        string bucketName,
        string objectKey,
        string? s3UploadId) : base(id)
    {
        EntityId = entityId;
        EntityType = entityType;
        ContentType = contentType;
        OwnerUserId = ownerUserId;
        FileName = fileName;
        ContentTypeMime = contentTypeMime;
        FileSize = expectedSize;
        UploadKind = uploadKind;
        BucketName = bucketName;
        ObjectName = objectKey;
        S3UploadId = s3UploadId;
        Status = ContentStatus.Pending;
    }

    // --- Identity / lineage ---
    public Guid EntityId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public ContentType ContentType { get; private set; }
    public string OwnerUserId { get; private set; } = string.Empty;

    // --- Lifecycle ---
    public ContentStatus Status { get; private set; }
    public UploadKind UploadKind { get; private set; }
    public DateTime? ValidatedAt { get; private set; }
    public string? QuarantineReason { get; private set; }
    public string? FailureReason { get; private set; }

    // --- Storage ---
    /// <summary>S3 multipart upload id; null for single-PUT uploads.</summary>
    public string? S3UploadId { get; private set; }
    public string BucketName { get; private set; } = string.Empty;
    /// <summary>Object key inside the bucket (per-user prefixed).</summary>
    public string ObjectName { get; private set; } = string.Empty;
    public string ETag { get; private set; } = string.Empty;
    public string? Sha256Checksum { get; private set; }

    // --- File metadata ---
    public string FileName { get; private set; } = string.Empty;
    public string ContentTypeMime { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;

    // --- Legacy fields kept for backward-compat with prior schema ---
    public string BlobName { get; private set; } = string.Empty;
    public string StorageDetails { get; private set; } = string.Empty;
    public string Path { get; private set; } = string.Empty;

    public IReadOnlyCollection<ContentMetadata> Metadata => _metadata.AsReadOnly();
    public IReadOnlyCollection<ContentVersion> Versions => _versions.AsReadOnly();

    // ------------------------------------------------------------------
    // Factory: a Pending row created at upload-init time. The bytes are
    // not yet in S3; only the intent to upload is recorded.
    // ------------------------------------------------------------------
    public static ContentEntity CreatePending(
        Guid entityId,
        string entityType,
        ContentType contentType,
        string ownerUserId,
        string fileName,
        string contentTypeMime,
        long expectedSize,
        UploadKind uploadKind,
        string bucketName,
        string objectKey,
        string? s3UploadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeMime);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (uploadKind == UploadKind.Multipart && string.IsNullOrWhiteSpace(s3UploadId))
            throw new ArgumentException("Multipart uploads require an S3 upload id.", nameof(s3UploadId));
        if (expectedSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedSize), "Expected size must be positive.");

        return new ContentEntity(
            id: Guid.NewGuid(),
            entityId, entityType, contentType, ownerUserId, fileName,
            contentTypeMime, expectedSize, uploadKind, bucketName, objectKey, s3UploadId);
    }

    // ------------------------------------------------------------------
    // State transitions. Each one verifies the source state to keep the
    // aggregate's invariants honest — a Quarantined row cannot become
    // Available, an Available row cannot be re-validated, etc.
    // ------------------------------------------------------------------
    public void MarkValidating()
    {
        if (Status != ContentStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot transition from {Status} to Validating; only Pending rows can be validated.");
        Status = ContentStatus.Validating;
    }

    public void MarkAvailable(
        string etag,
        string sha256Checksum,
        long actualSize,
        string url,
        DateTime utcNow)
    {
        if (Status != ContentStatus.Validating && Status != ContentStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot transition from {Status} to Available.");
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Checksum);

        ETag = etag;
        Sha256Checksum = sha256Checksum;
        FileSize = actualSize;
        Url = url ?? string.Empty;
        ValidatedAt = utcNow;
        Status = ContentStatus.Available;
    }

    public void Quarantine(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is ContentStatus.Quarantined or ContentStatus.Deleted)
            throw new InvalidOperationException(
                $"Cannot quarantine a row already in {Status}.");
        QuarantineReason = reason;
        Status = ContentStatus.Quarantined;
    }

    public void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is ContentStatus.Available or ContentStatus.Deleted or ContentStatus.Quarantined)
            throw new InvalidOperationException(
                $"Cannot fail a row already in {Status}.");
        FailureReason = reason;
        Status = ContentStatus.Failed;
    }

    public void SoftDelete()
    {
        if (Status == ContentStatus.Deleted) return;
        Status = ContentStatus.Deleted;
    }

    // ------------------------------------------------------------------
    // Aggregations (kept; behaviour unchanged from prior schema).
    // ------------------------------------------------------------------
    public void AddMetadata(ContentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata.Add(metadata);
    }

    public void AddVersion(ContentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _versions.Add(version);
    }

    public void SetSlug(string slug)
    {
        Slug = slug ?? string.Empty;
    }
}
