using Haworks.BuildingBlocks.Common;
using Haworks.Content.Application.DTOs;
using Haworks.Content.Application.Interfaces;
using Haworks.Content.Application.Options;
using Haworks.Content.Domain.Entities;
using Haworks.Content.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Haworks.Content.Application.Commands;

/// <summary>
/// Begins an upload. Creates a <see cref="ContentEntity"/> in the
/// <see cref="ContentStatus.Pending"/> state, decides whether the file
/// can use a single presigned PUT or needs S3 multipart, and returns
/// the URLs the client uses to push bytes directly to storage.
/// </summary>
public sealed record InitUploadCommand(
    Guid EntityId,
    string EntityType,
    string FileName,
    string ContentType,
    long TotalSize,
    string OwnerUserId) : IRequest<Result<InitUploadResultDto>>;

internal sealed class InitUploadCommandHandler(
    IContentStorageService storage,
    IContentRepository repository,
    IOptions<StorageOptions> storageOptions,
    ILogger<InitUploadCommandHandler> logger,
    TimeProvider time) : IRequestHandler<InitUploadCommand, Result<InitUploadResultDto>>
{
    // S3 multipart minimum part size (the last part may be smaller). We
    // stick to S3's spec floor so the same code works against AWS, R2,
    // Tigris, and LocalStack.
    private const long MultipartMinPartBytes = 5L * 1024 * 1024;

    public async Task<Result<InitUploadResultDto>> Handle(InitUploadCommand request, CancellationToken ct)
    {
        var opts = storageOptions.Value;
        var contentType = MapContentType(request.ContentType);
        var objectKey = BuildObjectKey(request.OwnerUserId, request.FileName);

        if (request.TotalSize <= opts.SinglePutMaxBytes)
        {
            var putUrl = await storage.GetPresignedPutUrlAsync(
                objectKey, request.ContentType, request.TotalSize,
                opts.PresignedUploadTtl, ct);

            var entity = ContentEntity.CreatePending(
                entityId: request.EntityId,
                entityType: request.EntityType,
                contentType: contentType,
                ownerUserId: request.OwnerUserId,
                fileName: request.FileName,
                contentTypeMime: request.ContentType,
                expectedSize: request.TotalSize,
                uploadKind: UploadKind.Single,
                bucketName: opts.BucketName,
                objectKey: objectKey,
                s3UploadId: null);

            await repository.AddContentAsync(entity, ct);
            await repository.SaveChangesAsync(ct);

            logger.LogInformation(
                "Init single-PUT upload {ContentId} ({Size} bytes) for {Owner}",
                entity.Id, request.TotalSize, request.OwnerUserId);

            return Result.Success(new InitUploadResultDto(
                ContentId: entity.Id,
                Kind: UploadKind.Single,
                PutUrl: putUrl,
                UploadId: null,
                PartUrls: null,
                PresignedUntilUtc: time.GetUtcNow().Add(opts.PresignedUploadTtl).UtcDateTime));
        }

        var partCount = (int)Math.Ceiling((double)request.TotalSize / MultipartMinPartBytes);
        var multipart = await storage.InitMultipartUploadAsync(
            objectKey, request.ContentType, partCount,
            opts.PresignedUploadTtl, ct);

        var multipartEntity = ContentEntity.CreatePending(
            entityId: request.EntityId,
            entityType: request.EntityType,
            contentType: contentType,
            ownerUserId: request.OwnerUserId,
            fileName: request.FileName,
            contentTypeMime: request.ContentType,
            expectedSize: request.TotalSize,
            uploadKind: UploadKind.Multipart,
            bucketName: opts.BucketName,
            objectKey: objectKey,
            s3UploadId: multipart.UploadId);

        await repository.AddContentAsync(multipartEntity, ct);
        await repository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Init multipart upload {ContentId} ({Size} bytes, {Parts} parts) for {Owner}",
            multipartEntity.Id, request.TotalSize, partCount, request.OwnerUserId);

        return Result.Success(new InitUploadResultDto(
            ContentId: multipartEntity.Id,
            Kind: UploadKind.Multipart,
            PutUrl: null,
            UploadId: multipart.UploadId,
            PartUrls: multipart.PartUrls
                .Select(p => new PresignedPartDto(p.PartNumber, p.Url))
                .ToArray(),
            PresignedUntilUtc: time.GetUtcNow().Add(opts.PresignedUploadTtl).UtcDateTime));
    }

    private static string BuildObjectKey(string userId, string fileName)
    {
        // Per-user prefix scopes presigned URLs to one tenant: a leaked
        // URL only exposes that user's intended object key, not anyone
        // else's. Random suffix avoids collisions when the same user
        // uploads two files with the same name.
        var safe = SanitizeFileName(fileName);
        return $"{userId}/{Guid.NewGuid():N}/{safe}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(fileName
            .Where(c => !invalid.Contains(c) && c < 128)
            .ToArray())
            .Replace(' ', '_');
        return clean.Length > 150 ? clean[..150] : clean;
    }

    private static ContentType MapContentType(string mime)
    {
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ContentType.Image;
        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return ContentType.Video;
        if (mime.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
            mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return ContentType.Document;
        return ContentType.Other;
    }
}
