using Haworks.BuildingBlocks.Common;
using Haworks.Content.Application.DTOs;
using Haworks.Content.Domain.Interfaces;
using MediatR;

namespace Haworks.Content.Application.Queries;

/// <summary>
/// Returns the lifecycle state of a <see cref="Domain.Entities.ContentEntity"/>
/// regardless of status. Used by clients to poll for finalisation,
/// inspect quarantine reason, or resume a multipart upload by checking
/// which parts the server has confirmed.
/// </summary>
public sealed record GetUploadStatusQuery(Guid ContentId, string OwnerUserId)
    : IRequest<Result<UploadStatusDto>>;

internal sealed class GetUploadStatusQueryHandler(IContentRepository repository)
    : IRequestHandler<GetUploadStatusQuery, Result<UploadStatusDto>>
{
    public async Task<Result<UploadStatusDto>> Handle(GetUploadStatusQuery request, CancellationToken ct)
    {
        var content = await repository.GetContentByIdAsync(request.ContentId, ct);
        if (content is null)
        {
            return Result.Failure<UploadStatusDto>(Error.Content.NotFound);
        }

        if (!string.Equals(content.OwnerUserId, request.OwnerUserId, StringComparison.Ordinal))
        {
            return Result.Failure<UploadStatusDto>(
                new Error("Content.Forbidden", "Caller does not own this upload.", ErrorType.Forbidden));
        }

        return Result.Success(new UploadStatusDto(
            ContentId: content.Id,
            Status: content.Status,
            FailureReason: content.FailureReason,
            QuarantineReason: content.QuarantineReason,
            ValidatedAt: content.ValidatedAt));
    }
}
