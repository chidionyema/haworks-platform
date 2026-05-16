using Haworks.Contracts.Media;

namespace Haworks.Media.Api.Infrastructure.Processing;

/// <summary>
/// Processes a media file after virus scan passes.
/// Returns a list of generated variants (thumbnails, HLS segments, normalized audio, etc.).
/// </summary>
public interface IMediaProcessor
{
    bool CanProcess(string mimeType);
    Task<IReadOnlyList<MediaVariant>> ProcessAsync(Guid mediaId, string s3Key, string mimeType, CancellationToken ct);
}
