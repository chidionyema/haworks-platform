namespace Haworks.Content.Domain.Entities;

/// <summary>
/// How the bytes of a <see cref="ContentEntity"/> are being uploaded.
/// Determines whether the server returns a single presigned PUT URL or
/// an S3 multipart-upload session with N presigned part URLs.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "S3 upload kinds; 'Single' refers to a single-PUT upload.")]
public enum UploadKind
{
    /// <summary>
    /// One presigned PUT URL. Used for files small enough that splitting
    /// adds overhead without resilience benefit (default threshold:
    /// <c>StorageOptions.SinglePutMaxBytes</c>).
    /// </summary>
    Single = 0,

    /// <summary>
    /// S3 multipart upload. Server initialises with
    /// <c>CreateMultipartUpload</c>, returns an upload id + N presigned
    /// part URLs. Client uploads parts (potentially in parallel),
    /// returns the per-part ETag list at completion. Server calls
    /// <c>CompleteMultipartUpload</c> server-side.
    /// </summary>
    Multipart = 1,
}
