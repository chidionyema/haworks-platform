using Microsoft.AspNetCore.Http;
using Haworks.Content.Application.Models;
using Haworks.Content.Domain.ValueObjects;

namespace Haworks.Content.Application.Interfaces;

// ---------------------------------------------------------------------------
// Storage gateway. The service is OUT OF the byte path: clients upload
// directly to S3 via presigned URLs. This interface exposes:
//   - Multipart-init (server-side CreateMultipartUpload)
//   - Single-PUT presign
//   - Per-part presign
//   - CompleteMultipartUpload (server stitches parts; no download)
//   - Abort (sweeper / explicit cancel)
//   - HEAD (verification + checksum read)
//   - Presigned GET (read path)
//   - Delete (soft-delete callbacks)
// ---------------------------------------------------------------------------
public interface IContentStorageService
{
    Task<string> GetPresignedPutUrlAsync(
        string objectKey,
        string contentType,
        long expectedSizeBytes,
        TimeSpan expiry,
        CancellationToken ct = default);

    Task<MultipartInitResult> InitMultipartUploadAsync(
        string objectKey,
        string contentType,
        int partCount,
        TimeSpan presignTtl,
        CancellationToken ct = default);

    Task<StorageObjectInfo> CompleteMultipartUploadAsync(
        string objectKey,
        string uploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken ct = default);

    Task AbortMultipartUploadAsync(
        string objectKey,
        string uploadId,
        CancellationToken ct = default);

    Task<StorageObjectInfo?> HeadAsync(string objectKey, CancellationToken ct = default);

    Task<string> GetPresignedGetUrlAsync(
        string objectKey,
        TimeSpan expiry,
        CancellationToken ct = default);

    Task DeleteAsync(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Compute SHA-256 of the object by streaming it server-side. Used
    /// at validation time after client has finished uploading.
    /// </summary>
    Task<string> ComputeSha256Async(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Move an object from its current key to a quarantine prefix.
    /// Used by the validator when virus / signature checks fail.
    /// </summary>
    Task QuarantineAsync(string objectKey, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Validation. Runs after CompleteMultipartUpload (or after the client
// signals a single-PUT is done). Composes IFileSignatureValidator +
// IVirusScanner; returns a verdict the handler uses to transition the
// ContentEntity to Available or Quarantined.
// ---------------------------------------------------------------------------
public interface IUploadValidator
{
    Task<UploadValidationResult> ValidateAsync(
        string objectKey,
        string declaredContentType,
        CancellationToken ct = default);
}

public sealed record UploadValidationResult(
    bool IsValid,
    string? FailureReason,
    string Sha256Checksum,
    long SizeBytes,
    string ETag);

// ---------------------------------------------------------------------------
// Validators (kept; their signatures are unchanged).
// ---------------------------------------------------------------------------
public interface IFileValidator
{
    Task<FileValidationResult> ValidateAsync(IFormFile file);
    Task<FileValidationResult> ValidateMetadataAsync(string fileName, string contentType, long totalSize);
}

public interface IFileSignatureValidator
{
    Task<FileSignatureValidationResult> ValidateAsync(Stream fileStream);
}

public interface IVirusScanner
{
    Task<VirusScanResult> ScanAsync(Stream fileStream);
}
