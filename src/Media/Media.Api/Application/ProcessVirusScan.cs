using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Infrastructure.Processing;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Media.Api.Application;

public record ProcessVirusScanCommand(Guid MediaId) : IRequest<Result<Unit>>;

public class ProcessVirusScanValidator : AbstractValidator<ProcessVirusScanCommand>
{
    public ProcessVirusScanValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
    }
}

public class ProcessVirusScanHandler : IRequestHandler<ProcessVirusScanCommand, Result<Unit>>
{
    private readonly MediaDbContext _context;
    private readonly IVirusScanner _virusScanner;
    private readonly ICurrentUserService _currentUser;
    private readonly IS3Service _s3;
    private readonly MediaProcessingOrchestrator _orchestrator;
    private readonly IPublishEndpoint _publisher;

    public ProcessVirusScanHandler(
        MediaDbContext context,
        IVirusScanner virusScanner,
        ICurrentUserService currentUser,
        IS3Service s3,
        MediaProcessingOrchestrator orchestrator,
        IPublishEndpoint publisher)
    {
        _context = context;
        _virusScanner = virusScanner;
        _currentUser = currentUser;
        _s3 = s3;
        _orchestrator = orchestrator;
        _publisher = publisher;
    }

    public async Task<Result<Unit>> Handle(ProcessVirusScanCommand request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.UserId;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Result.Failure<MediatR.Unit>(new Error("Media.Unauthorized", "Authenticated user identity could not be resolved."));
        }

        var mediaFile = await _context.MediaFiles
            .FirstOrDefaultAsync(f => f.Id == request.MediaId, cancellationToken);

        if (mediaFile == null)
        {
            return Result.Failure<MediatR.Unit>(new Error("Media.NotFound", "Media file not found."));
        }

        if (!string.Equals(mediaFile.OwnerId, ownerId, StringComparison.Ordinal))
        {
            return Result.Failure<MediatR.Unit>(new Error("Media.Forbidden", "You do not own this media file."));
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            mediaFile.MarkAsQuarantined();
            await _context.SaveChangesAsync(cancellationToken);

            await using var stream = await _s3.DownloadAsync(mediaFile.Id.ToString(), cancellationToken);
            var isClean = await _virusScanner.ScanAsync(stream, cancellationToken);

            if (isClean)
            {
                mediaFile.MarkAsActive();
                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                await _publisher.Publish(new MediaScanPassedEvent
                {
                    MediaId = mediaFile.Id,
                    OwnerId = mediaFile.OwnerId,
                    FileName = mediaFile.FileName,
                    MimeType = mediaFile.MimeType,
                    Size = mediaFile.Size,
                }, cancellationToken);

                // Trigger processing pipeline (transcode, thumbnails, audio normalization)
                try
                {
                    var variants = await _orchestrator.ProcessAsync(
                        mediaFile.Id, mediaFile.Id.ToString(), mediaFile.MimeType, cancellationToken);

                    if (variants.Count > 0)
                    {
                        await _publisher.Publish(new MediaProcessingCompletedEvent
                        {
                            MediaId = mediaFile.Id,
                            OwnerId = mediaFile.OwnerId,
                            FileName = mediaFile.FileName,
                            Variants = variants,
                        }, cancellationToken);
                    }
                }
                catch (Exception)
                {
                    // Processing failure doesn't block the scan result — file is still Active
                    await _publisher.Publish(new MediaProcessingFailedEvent
                    {
                        MediaId = mediaFile.Id,
                        OwnerId = mediaFile.OwnerId,
                        FileName = mediaFile.FileName,
                        Reason = "Media processing failed. Original file is still available.",
                    }, cancellationToken);
                }
            }
            else
            {
                mediaFile.MarkAsRejected();
                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                await _publisher.Publish(new MediaScanFailedEvent
                {
                    MediaId = mediaFile.Id,
                    OwnerId = mediaFile.OwnerId,
                    FileName = mediaFile.FileName,
                    Reason = "Virus detected or scan failed.",
                }, cancellationToken);
            }
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Unit.Value;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return Unit.Value;
    }
}
