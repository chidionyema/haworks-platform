using Haworks.BuildingBlocks.Common;
using Haworks.Content.Application.Interfaces;
using Haworks.Content.Domain.Entities;
using Haworks.Content.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Haworks.Content.Application.Commands;

/// <summary>
/// Cancels an in-flight upload. Aborts the S3 multipart upload (if any)
/// and marks the row Failed. Safe to call from a UI "cancel" button or
/// from a client that abandoned the upload before completing it.
/// </summary>
public sealed record AbortUploadCommand(Guid ContentId, string OwnerUserId) : IRequest<Result>;

internal sealed class AbortUploadCommandHandler(
    IContentStorageService storage,
    IContentRepository repository,
    ILogger<AbortUploadCommandHandler> logger) : IRequestHandler<AbortUploadCommand, Result>
{
    public async Task<Result> Handle(AbortUploadCommand request, CancellationToken ct)
    {
        var content = await repository.GetContentByIdTrackedAsync(request.ContentId, ct);
        if (content is null)
        {
            return Result.Failure(Error.Content.NotFound);
        }

        if (!string.Equals(content.OwnerUserId, request.OwnerUserId, StringComparison.Ordinal))
        {
            return Result.Failure(
                new Error("Content.Forbidden", "Caller does not own this upload.", ErrorType.Forbidden));
        }

        if (content.Status != ContentStatus.Pending)
        {
            // Nothing to abort — already finalised, quarantined, or
            // already failed/deleted. Idempotent success.
            return Result.Success();
        }

        if (content.UploadKind == UploadKind.Multipart && !string.IsNullOrEmpty(content.S3UploadId))
        {
            try
            {
                await storage.AbortMultipartUploadAsync(content.ObjectName, content.S3UploadId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AbortMultipartUpload failed for {ContentId}; row still marked Failed (sweeper will retry abort)",
                    content.Id);
            }
        }

        content.Fail("Upload aborted by caller.");
        await repository.SaveChangesAsync(ct);
        return Result.Success();
    }
}
