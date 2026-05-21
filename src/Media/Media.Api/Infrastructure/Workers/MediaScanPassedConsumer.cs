using Haworks.Contracts.Media;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// Consumes <see cref="MediaScanPassedEvent"/> to move files from quarantine
/// to the public bucket and mark the media record as Active.
/// Without this consumer, scanned files remain quarantined indefinitely.
/// </summary>
public sealed class MediaScanPassedConsumer(
    MediaDbContext context,
    IS3Service s3,
    ILogger<MediaScanPassedConsumer> logger) : IConsumer<MediaScanPassedEvent>
{
    public async Task Consume(ConsumeContext<MediaScanPassedEvent> ctx)
    {
        var msg = ctx.Message;
        var file = await context.MediaFiles
            .FirstOrDefaultAsync(f => f.Id == msg.MediaId, ctx.CancellationToken);

        if (file is null)
        {
            logger.LogWarning("MediaScanPassed for unknown MediaId {MediaId}", msg.MediaId);
            return;
        }

        if (file.Status == Domain.MediaStatus.Active)
        {
            logger.LogInformation("Media {MediaId} already Active; idempotent skip", msg.MediaId);
            return;
        }

        // Move from quarantine to public bucket
        await s3.PromoteFromQuarantineAsync(file.ObjectName, ctx.CancellationToken);

        file.MarkAsActive();

        logger.LogInformation("Media {MediaId} promoted from quarantine to Active", msg.MediaId);
        // MassTransit EF Outbox commits automatically
    }
}
