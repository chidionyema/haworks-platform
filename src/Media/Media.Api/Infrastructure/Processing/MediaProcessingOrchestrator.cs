using Haworks.Contracts.Media;

namespace Haworks.Media.Api.Infrastructure.Processing;

/// <summary>
/// Orchestrates media processing after a file passes virus scan.
/// Iterates registered IMediaProcessor implementations (video, image, audio)
/// and collects all generated variants.
/// </summary>
public sealed class MediaProcessingOrchestrator(
    IEnumerable<IMediaProcessor> processors,
    ILogger<MediaProcessingOrchestrator> logger)
{
    public async Task<IReadOnlyList<MediaVariant>> ProcessAsync(
        Guid mediaId, string s3Key, string mimeType, CancellationToken ct)
    {
        var applicableProcessors = processors.Where(p => p.CanProcess(mimeType)).ToList();

        if (applicableProcessors.Count == 0)
        {
            logger.LogInformation("No processors registered for {MimeType} — file {MediaId} served as-is", mimeType, mediaId);
            return Array.Empty<MediaVariant>();
        }

        var allVariants = new List<MediaVariant>();

        foreach (var processor in applicableProcessors)
        {
            try
            {
                var variants = await processor.ProcessAsync(mediaId, s3Key, mimeType, ct);
                allVariants.AddRange(variants);
                logger.LogInformation("Processor {Type} generated {Count} variants for {MediaId}",
                    processor.GetType().Name, variants.Count, mediaId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processor {Type} failed for {MediaId}", processor.GetType().Name, mediaId);
                throw;
            }
        }

        return allVariants;
    }
}
