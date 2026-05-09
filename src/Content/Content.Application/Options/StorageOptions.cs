using System.ComponentModel.DataAnnotations;
using Haworks.Content.Domain.Entities;

namespace Haworks.Content.Application.Options;

/// <summary>
/// S3-compatible storage configuration. The same shape is used for
/// Tigris (prod), LocalStack (test/dev), and any other S3-shaped
/// endpoint — the difference is config, not code.
///
/// Bound from the <c>Storage</c> section. <c>ValidateOnStart()</c> is
/// applied so a missing key fails the host build, not the first request.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>S3 endpoint URL (Tigris, LocalStack, AWS, R2, …).</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    public string BucketName { get; set; } = string.Empty;

    /// <summary><c>auto</c> for Tigris/R2; an explicit AWS region for AWS S3; <c>us-east-1</c> for LocalStack.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// LocalStack and bucket-as-folder S3 backends require path-style
    /// addressing. Tigris / AWS S3 / R2 prefer virtual-hosted-style.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>Default TTL for presigned upload URLs.</summary>
    public TimeSpan PresignedUploadTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Default TTL for presigned download URLs.</summary>
    public TimeSpan PresignedDownloadTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Files at or below this size use <see cref="UploadKind.Single"/>
    /// (one presigned PUT). Larger files use S3 multipart with presigned
    /// part URLs. Default: 8 MiB — comfortably below S3's 5 MiB minimum
    /// part size and above the typical "small image" threshold.
    /// </summary>
    [Range(1024, long.MaxValue)]
    public long SinglePutMaxBytes { get; set; } = 8L * 1024 * 1024;

    /// <summary>
    /// How long a Pending row stays sweepable. After this, the sweeper
    /// aborts the S3 multipart upload (if any) and marks the row Failed.
    /// </summary>
    public TimeSpan PendingUploadTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How often the sweeper runs.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}
