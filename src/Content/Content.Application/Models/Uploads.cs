namespace Haworks.Content.Application.Models;

/// <summary>One presigned URL for an S3 multipart part.</summary>
public sealed record PresignedPartUrl(int PartNumber, string Url);

/// <summary>Result of <c>CreateMultipartUpload</c> + presigned-part minting.</summary>
public sealed record MultipartInitResult(
    string UploadId,
    IReadOnlyList<PresignedPartUrl> PartUrls);

/// <summary>One uploaded part as reported back by the client at completion time.</summary>
public sealed record UploadedPart(int PartNumber, string ETag);

/// <summary>
/// Server-side description of the finalised object after a successful
/// upload + validation pass. Returned to the controller; persisted on
/// the <see cref="Domain.Entities.ContentEntity"/>.
/// </summary>
public sealed record StorageObjectInfo(
    string BucketName,
    string ObjectKey,
    string ETag,
    long SizeBytes,
    string ContentType);
