using Amazon.S3;
using Haworks.Content.Application.Interfaces;
using Haworks.Content.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Haworks.Content.Infrastructure.ExternalServices.Validation;

/// <summary>
/// Post-upload validation pipeline. Streams the object once, runs:
///   1. Magic-byte / signature check via <see cref="IFileSignatureValidator"/>
///   2. Virus scan via <see cref="IVirusScanner"/>
///   3. SHA-256 checksum capture
/// Reports a single verdict; the caller transitions the
/// <see cref="Domain.Entities.ContentEntity"/> accordingly.
/// </summary>
internal sealed class UploadValidator(
    IAmazonS3 s3,
    IOptions<StorageOptions> storageOptions,
    IFileSignatureValidator signatureValidator,
    IVirusScanner virusScanner,
    IContentStorageService storage,
    ILogger<UploadValidator> logger) : IUploadValidator
{
    public async Task<UploadValidationResult> ValidateAsync(
        string objectKey, string declaredContentType, CancellationToken ct = default)
    {
        var bucket = storageOptions.Value.BucketName;

        // We pull the object once to compute SHA-256 in the same pass —
        // signature check and virus scan are then run against in-memory
        // copies of small files; for large files they re-stream via S3.
        // This keeps the small-file path fast without buffering huge
        // files in memory.
        var head = await storage.HeadAsync(objectKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Cannot validate {objectKey}: object missing in S3.");

        // 1. Magic-byte signature check.
        await using (var sigStream = await OpenAsync(bucket, objectKey, ct).ConfigureAwait(false))
        {
            var sig = await signatureValidator.ValidateAsync(sigStream).ConfigureAwait(false);
            if (!sig.IsValid)
            {
                logger.LogWarning(
                    "Signature check failed for {Key}: declared {Declared}, detected {Detected}",
                    objectKey, declaredContentType, sig.FileType);
                return new UploadValidationResult(
                    IsValid: false,
                    FailureReason: $"File signature mismatch (detected: {sig.FileType}).",
                    Sha256Checksum: string.Empty,
                    SizeBytes: head.SizeBytes,
                    ETag: head.ETag);
            }
        }

        // 2. Virus scan.
        await using (var virusStream = await OpenAsync(bucket, objectKey, ct).ConfigureAwait(false))
        {
            var scan = await virusScanner.ScanAsync(virusStream).ConfigureAwait(false);
            if (scan.IsMalicious)
            {
                logger.LogCritical(
                    "Virus detected in {Key}: {Threat}", objectKey, scan.ThreatName);
                return new UploadValidationResult(
                    IsValid: false,
                    FailureReason: $"Virus detected: {scan.ThreatName}.",
                    Sha256Checksum: string.Empty,
                    SizeBytes: head.SizeBytes,
                    ETag: head.ETag);
            }
        }

        // 3. SHA-256 checksum.
        var sha256 = await storage.ComputeSha256Async(objectKey, ct).ConfigureAwait(false);

        return new UploadValidationResult(
            IsValid: true,
            FailureReason: null,
            Sha256Checksum: sha256,
            SizeBytes: head.SizeBytes,
            ETag: head.ETag);
    }

    private async Task<Stream> OpenAsync(string bucket, string key, CancellationToken ct)
    {
        var resp = await s3.GetObjectAsync(bucket, key, ct).ConfigureAwait(false);
        return resp.ResponseStream;
    }
}
