using Haworks.BuildingBlocks.Common;
using Haworks.Content.Application.DTOs;
using Haworks.Content.Application.Interfaces;
using Haworks.Content.Application.Options;
using Haworks.Content.Domain.Entities;
using Haworks.Content.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Options;

namespace Haworks.Content.Application.Queries;

public sealed record GetContentQuery(Guid ContentId) : IRequest<Result<ContentDto>>;

internal sealed class GetContentQueryHandler(
    IContentRepository repository,
    IContentStorageService storage,
    IOptions<StorageOptions> storageOptions) : IRequestHandler<GetContentQuery, Result<ContentDto>>
{
    public async Task<Result<ContentDto>> Handle(GetContentQuery request, CancellationToken ct)
    {
        var content = await repository.GetContentByIdAsync(request.ContentId, ct);
        if (content is null || content.Status != ContentStatus.Available)
        {
            return Result.Failure<ContentDto>(Error.Content.NotFound);
        }

        // Mint a fresh presigned GET URL on every read. The cached Url
        // on the entity expires; a fresh one keeps the link valid for
        // the configured download TTL without requiring writes here.
        var downloadUrl = await storage.GetPresignedGetUrlAsync(
            content.ObjectName, storageOptions.Value.PresignedDownloadTtl, ct);

        return Result.Success(new ContentDto(
            Id: content.Id,
            EntityId: content.EntityId,
            EntityType: content.EntityType,
            DownloadUrl: downloadUrl,
            ContentType: content.ContentTypeMime,
            FileSize: content.FileSize,
            ETag: content.ETag,
            Sha256Checksum: content.Sha256Checksum,
            ValidatedAt: content.ValidatedAt));
    }
}
