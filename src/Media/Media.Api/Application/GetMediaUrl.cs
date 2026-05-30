using Haworks.BuildingBlocks.CurrentUser;

namespace Haworks.Media.Api.Application;

public record GetMediaUrlQuery(Guid MediaId, string? Variant = null) : IRequest<Result<MediaUrlResponse>>;
public record MediaUrlResponse(string Url, DateTime ExpiresAt);

public partial class GetMediaUrlValidator : AbstractValidator<GetMediaUrlQuery>
{
    private static readonly HashSet<string> AllowedVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audio variants
        "audio-normalized",

        // Video variants
        "hls-master",
        "hls-1080p", "hls-720p", "hls-480p", "hls-360p",

        // Image variants (common thumbnail sizes)
        "thumbnail-50", "thumbnail-150", "thumbnail-300", "thumbnail-500", "thumbnail-1000",
        "webp-50", "webp-150", "webp-300", "webp-500", "webp-1000"
    };

    public GetMediaUrlValidator()
    {
        RuleFor(x => x.MediaId).NotEmpty();
        RuleFor(x => x.Variant)
            .Must(v => v == null || AllowedVariants.Contains(v))
            .WithMessage("Invalid variant. Allowed variants: audio-normalized, hls-master, hls-{quality}, thumbnail-{size}, webp-{size}");
    }
}

public class GetMediaUrlHandler(
    MediaDbContext context,
    IS3Service s3,
    ICurrentUserService currentUser) : IRequestHandler<GetMediaUrlQuery, Result<MediaUrlResponse>>
{
    public async Task<Result<MediaUrlResponse>> Handle(GetMediaUrlQuery request, CancellationToken ct)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrEmpty(ownerId))
            return Result.Failure<MediaUrlResponse>(new Error("Media.Unauthorized", "Authenticated user identity could not be resolved."));

        var file = await context.MediaFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.MediaId, ct);

        if (file == null)
            return Result.Failure<MediaUrlResponse>(new Error("Media.NotFound", "Media file not found."));

        if (!string.Equals(file.OwnerId, ownerId, StringComparison.Ordinal))
            return Result.Failure<MediaUrlResponse>(new Error("Media.Forbidden", "You do not own this media file."));

        if (file.Status != MediaStatus.Active)
            return Result.Failure<MediaUrlResponse>(new Error("Media.NotReady",
                $"File is in {file.Status} state and cannot be served."));

        var s3Key = string.IsNullOrEmpty(request.Variant)
            ? file.Id.ToString()
            : $"media/{file.Id}/{request.Variant}";

        var url = s3.GeneratePresignedGetUrl(s3Key);
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        return new MediaUrlResponse(url, expiresAt);
    }
}
