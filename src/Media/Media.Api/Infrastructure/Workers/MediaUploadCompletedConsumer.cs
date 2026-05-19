using Haworks.Contracts.Media;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// Consumes <see cref="MediaUploadCompletedEvent"/> from the S3EventConsumer path.
/// Runs the same scan + event pipeline as the HTTP path without requiring
/// an authenticated HTTP user context (the OwnerId comes from the event payload).
///
/// Law #1: NO manual BeginTransactionAsync — MassTransit EF Outbox manages the ambient transaction.
/// Law #2: Publish via ConsumeContext, not IPublishEndpoint — ensures outbox captures messages.
/// </summary>
public sealed class MediaUploadCompletedConsumer(
    MediaDbContext context,
    IVirusScanner virusScanner,
    IS3Service s3,
    ILogger<MediaUploadCompletedConsumer> logger) : IConsumer<MediaUploadCompletedEvent>
{
    public async Task Consume(ConsumeContext<MediaUploadCompletedEvent> ctx)
    {
        var msg = ctx.Message;
        var ct = ctx.CancellationToken;

        var file = await context.MediaFiles.FirstOrDefaultAsync(f => f.Id == msg.MediaId, ct);
        if (file == null)
        {
            logger.LogDebug("MediaUploadCompletedEvent for unknown media {MediaId} — skipping", msg.MediaId);
            return;
        }

        if (file.Status != Domain.MediaStatus.Pending)
        {
            logger.LogDebug("Media {MediaId} already processed (status={Status}) — skipping", msg.MediaId, file.Status);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"media-scan-{Guid.NewGuid()}");
        try
        {
            await s3.DownloadToFileAsync(file.Id.ToString(), tempPath, ct);

            // Hash verification
            await using (var hashStream = File.OpenRead(tempPath))
            {
                var actualHash = Convert.ToHexStringLower(
                    await System.Security.Cryptography.SHA256.HashDataAsync(hashStream, ct));
                if (!string.Equals(actualHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    file.MarkAsQuarantined();
                    file.MarkAsRejected();

                    await ctx.Publish(new MediaScanFailedEvent
                    {
                        MediaId = file.Id,
                        OwnerId = file.OwnerId,
                        FileName = file.FileName,
                        Reason = "Server-side hash does not match declared hash.",
                    }, ct);

                    logger.LogWarning("Hash mismatch for {MediaId} via S3 event path", msg.MediaId);
                    return;
                }
            }

            // Virus scan
            var isClean = await virusScanner.ScanFileAsync(tempPath, ct);

            file.MarkAsQuarantined();

            if (isClean)
            {
                file.MarkAsActive();

                await ctx.Publish(new MediaScanPassedEvent
                {
                    MediaId = file.Id,
                    OwnerId = file.OwnerId,
                    FileName = file.FileName,
                    MimeType = file.MimeType,
                    Size = file.Size,
                }, ct);

                await ctx.Send(new Uri("queue:process-media-command"), new ProcessMediaCommand
                {
                    MediaId = file.Id,
                    OwnerId = file.OwnerId,
                    FileName = file.FileName,
                    MimeType = file.MimeType,
                    S3Key = file.Id.ToString(),
                });
            }
            else
            {
                file.MarkAsRejected();

                await ctx.Publish(new MediaScanFailedEvent
                {
                    MediaId = file.Id,
                    OwnerId = file.OwnerId,
                    FileName = file.FileName,
                    Reason = "Virus detected or scan failed.",
                }, ct);
            }

            logger.LogInformation("S3 event scan complete for {MediaId}: {Status}", msg.MediaId, file.Status);
        }
        finally
        {
            try { File.Delete(tempPath); } catch (IOException ex) { logger.LogWarning(ex, "Failed to delete temporary file {TempPath}", tempPath); }
        }
    }
}
