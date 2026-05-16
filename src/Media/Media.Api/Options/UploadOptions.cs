using System.ComponentModel.DataAnnotations;

namespace Haworks.Media.Api.Options;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>Files smaller than this use single-part PUT. Larger use multipart. Default 8MB.</summary>
    public long SinglePutMaxBytes { get; set; } = 8_388_608;

    /// <summary>Size of each multipart part in bytes. Default 26MB (allows 256GB in 10,000 parts).</summary>
    public long PartSizeBytes { get; set; } = 26_214_400;

    /// <summary>Maximum file size in bytes. Default 256GB.</summary>
    public long MaxFileSizeBytes { get; set; } = 274_877_906_944;

    /// <summary>How long before stale pending uploads are swept. Default 6 hours.</summary>
    public int PendingUploadTtlHours { get; set; } = 6;

    /// <summary>Upload sweeper polling interval. Default 5 minutes.</summary>
    public int SweeperIntervalMinutes { get; set; } = 5;

    /// <summary>Presigned URL expiry in minutes. Default 60.</summary>
    public int PresignedUrlExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Allowed MIME types. If empty, all types are allowed (not recommended for production).
    /// </summary>
    public List<string> AllowedMimeTypes { get; set; } =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/avif", "image/svg+xml",
        "video/mp4", "video/quicktime", "video/x-msvideo", "video/x-matroska", "video/webm",
        "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/flac", "audio/webm",
        "application/pdf"
    ];
}
