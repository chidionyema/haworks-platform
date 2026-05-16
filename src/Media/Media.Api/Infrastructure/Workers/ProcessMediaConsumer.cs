using Haworks.Contracts.Media;
using Haworks.Media.Api.Infrastructure.Processing;
using MassTransit;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// MassTransit consumer that runs the media processing pipeline (transcode, thumbnails, normalization)
/// asynchronously — never in the HTTP request path.
/// </summary>
public sealed class ProcessMediaConsumer(
    MediaProcessingOrchestrator orchestrator,
    IPublishEndpoint publisher,
    ILogger<ProcessMediaConsumer> logger) : IConsumer<ProcessMediaCommand>
{
    public async Task Consume(ConsumeContext<ProcessMediaCommand> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        logger.LogInformation("Starting async media processing for {MediaId} ({MimeType})", msg.MediaId, msg.MimeType);

        var variants = await orchestrator.ProcessAsync(msg.MediaId, msg.S3Key, msg.MimeType, ct);

        if (variants.Count > 0)
        {
            await publisher.Publish(new MediaProcessingCompletedEvent
            {
                MediaId = msg.MediaId,
                OwnerId = msg.OwnerId,
                FileName = msg.FileName,
                Variants = variants,
            }, ct);
        }

        logger.LogInformation("Media processing completed for {MediaId}: {Count} variants", msg.MediaId, variants.Count);
    }
}
