using System.ComponentModel.DataAnnotations;
using Haworks.Content.Domain.Entities;

namespace Haworks.Content.Application.DTOs;

/// <summary>
/// Canonical read shape of a finalised <see cref="ContentEntity"/>. Only
/// returned for <see cref="ContentStatus.Available"/> rows; other states
/// surface via <see cref="UploadStatusDto"/>.
/// </summary>
public sealed record ContentDto(
    Guid Id,
    Guid EntityId,
    string EntityType,
    string DownloadUrl,
    string ContentType,
    long FileSize,
    string ETag,
    string? Sha256Checksum,
    DateTime? ValidatedAt);

/// <summary>
/// Returned by <c>POST /api/v1/content/uploads</c>. Holds everything the
/// client needs to start uploading bytes directly to S3.
///
/// For <see cref="UploadKind.Single"/>: <c>PutUrl</c> is populated;
/// <c>UploadId</c> and <c>PartUrls</c> are null.
/// For <see cref="UploadKind.Multipart"/>: <c>UploadId</c> and
/// <c>PartUrls</c> are populated; <c>PutUrl</c> is null.
/// </summary>
public sealed record InitUploadResultDto(
    Guid ContentId,
    UploadKind Kind,
    string? PutUrl,
    string? UploadId,
    IReadOnlyList<PresignedPartDto>? PartUrls,
    DateTime PresignedUntilUtc);

public sealed record PresignedPartDto(int PartNumber, string Url);

/// <summary>
/// Returned by <c>POST /api/v1/content/uploads/{id}/complete</c> for a
/// successful finalisation, or by <c>GET /uploads/{id}</c> for status
/// queries before completion.
/// </summary>
public sealed record UploadStatusDto(
    Guid ContentId,
    ContentStatus Status,
    string? FailureReason,
    string? QuarantineReason,
    DateTime? ValidatedAt);

public sealed record InitUploadRequestDto(
    [Required] Guid EntityId,
    [Required] string EntityType,
    [Required] string FileName,
    [Required] string ContentType,
    [Range(1, long.MaxValue)] long TotalSize);

/// <summary>
/// Body of <c>POST /uploads/{id}/complete</c>. <c>Parts</c> is required
/// for multipart uploads, null/empty for single-PUT.
/// </summary>
public sealed record CompleteUploadRequestDto(
    IReadOnlyList<UploadedPartDto>? Parts);

public sealed record UploadedPartDto(int PartNumber, string ETag);
