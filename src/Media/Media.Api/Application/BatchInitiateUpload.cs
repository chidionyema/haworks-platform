using Haworks.BuildingBlocks.CurrentUser;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Media.Api.Options;
using Microsoft.Extensions.Options;

namespace Haworks.Media.Api.Application;

public record BatchInitiateUploadCommand(IReadOnlyList<InitiateUploadCommand> Files, string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result<IReadOnlyList<UploadResponse>>>;

public class BatchInitiateUploadValidator : AbstractValidator<BatchInitiateUploadCommand>
{
    public BatchInitiateUploadValidator(IOptions<UploadOptions> opts)
    {
        RuleFor(x => x.Files).NotEmpty()
            .Must(f => f.Count <= 50).WithMessage("Maximum 50 files per batch.");
        RuleForEach(x => x.Files).SetValidator(new InitiateUploadValidator(opts));
    }
}

public class BatchInitiateUploadHandler(
    MediaDbContext context,
    IS3Service s3,
    ICurrentUserService currentUser,
    IOptions<UploadOptions> uploadOpts) : IRequestHandler<BatchInitiateUploadCommand, Result<IReadOnlyList<UploadResponse>>>
{
    private readonly UploadOptions _opts = uploadOpts.Value;

    public async Task<Result<IReadOnlyList<UploadResponse>>> Handle(BatchInitiateUploadCommand request, CancellationToken ct)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrEmpty(ownerId))
            return Result.Failure<IReadOnlyList<UploadResponse>>(new Error("Media.Unauthorized", "Authenticated user identity could not be resolved."));

        // Fix N+1: Get all existing files with matching hashes in a single query
        var requestHashes = request.Files.Select(f => f.Hash).ToHashSet();
        var existingFiles = await context.MediaFiles
            .Where(f => requestHashes.Contains(f.Hash) && f.OwnerId == ownerId)
            .ToDictionaryAsync(f => f.Hash, f => f, ct);

        var responses = new List<UploadResponse>(request.Files.Count);
        var newMediaFiles = new List<MediaFile>();

        foreach (var file in request.Files)
        {
            if (existingFiles.TryGetValue(file.Hash, out var existing))
            {
                responses.Add(new UploadResponse(existing.Id, null, true));
                continue;
            }

            var mediaFile = MediaFile.Create(file.FileName, file.Hash, file.Size, file.MimeType, ownerId);
            var key = mediaFile.Id.ToString();

            if (file.Size <= _opts.SinglePutMaxBytes)
            {
                newMediaFiles.Add(mediaFile);
                var uploadUrl = s3.GeneratePreSignedUrl(key, mediaFile.MimeType);
                responses.Add(new UploadResponse(mediaFile.Id, uploadUrl, false));
            }
            else
            {
                var partCount = (int)Math.Ceiling((double)file.Size / _opts.PartSizeBytes);
                var s3UploadId = await s3.InitiateMultipartUploadAsync(key, file.MimeType, ct);
                mediaFile.InitiateMultipart(s3UploadId, partCount);
                newMediaFiles.Add(mediaFile);

                var partUrls = Enumerable.Range(1, partCount)
                    .Select(i => s3.GeneratePartPresignedUrl(key, s3UploadId, i))
                    .ToList();

                responses.Add(new UploadResponse(mediaFile.Id, null, false, true, s3UploadId, partCount, partUrls));
            }
        }

        // Fix atomicity: Single SaveChangesAsync for all new files
        if (newMediaFiles.Count > 0)
        {
            context.MediaFiles.AddRange(newMediaFiles);
            await context.SaveChangesAsync(ct);
        }

        return responses;
    }
}
