using Haworks.Contracts.Media;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// Consumes <see cref="MediaScanFailedEvent"/> to reject the media record
/// and delete the quarantined file from S3.
/// </summary>
public sealed class MediaScanFailedConsumer(
    MediaDbContext context,
    IS3Service s3,
    ILogger<MediaScanFailedConsumer> logger) : IConsumer<MediaScanFailedEvent>
{
    public async Task Consume(ConsumeContext<MediaScanFailedEvent> ctx)
    {
        var msg = ctx.Message;
        var file = await context.MediaFiles
            .FirstOrDefaultAsync(f => f.Id == msg.MediaId, ctx.CancellationToken);

        if (file is null)
        {
            logger.LogWarning("MediaScanFailed for unknown MediaId {MediaId}", msg.MediaId);
            return;
        }

        if (file.Status == Domain.MediaStatus.Rejected)
        {
            logger.LogInformation("Media {MediaId} already Rejected; idempotent skip", msg.MediaId);
            return;
        }

        file.MarkAsRejected();

        // Delete quarantined file
        await s3.DeleteAsync($"quarantine/{file.ObjectName}", ctx.CancellationToken);

        logger.LogWarning("Media {MediaId} rejected: {Reason}. Quarantine file deleted.", msg.MediaId, msg.Reason);
    }
}
