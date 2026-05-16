using Haworks.Contracts.Media;
using Haworks.Media.Api.Options;
using Microsoft.Extensions.Options;

namespace Haworks.Media.Api.Infrastructure.Processing;

public sealed class VideoProcessor(
    FfmpegService ffmpeg,
    IS3Service s3,
    IOptions<TranscodeOptions> opts,
    ILogger<VideoProcessor> logger) : IMediaProcessor
{
    private readonly TranscodeOptions _opts = opts.Value;

    public bool CanProcess(string mimeType) =>
        _opts.Enabled && mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<MediaVariant>> ProcessAsync(
        Guid mediaId, string s3Key, string mimeType, CancellationToken ct)
    {
        var workDir = Path.Combine(_opts.TempDirectory, mediaId.ToString());
        Directory.CreateDirectory(workDir);

        try
        {
            var inputPath = Path.Combine(workDir, "input");
            await DownloadToFileAsync(s3Key, inputPath, ct);

            var probe = await ffmpeg.ProbeAsync(inputPath, ct);
            if (probe == null)
            {
                logger.LogWarning("FFprobe failed for {MediaId} — file may be corrupt", mediaId);
                throw new InvalidOperationException("Video file is corrupt or unsupported.");
            }

            if (probe.DurationSeconds.HasValue && probe.DurationSeconds.Value / 60 > _opts.MaxDurationMinutes)
            {
                throw new InvalidOperationException(
                    $"Video duration ({probe.DurationSeconds.Value / 60:F0}min) exceeds max ({_opts.MaxDurationMinutes}min).");
            }

            var sourceHeight = probe.Height ?? 0;
            var variants = new List<MediaVariant>();

            foreach (var tier in _opts.QualityTiers.Where(t => sourceHeight >= t.MinSourceHeight))
            {
                var tierDir = Path.Combine(workDir, tier.Name);
                var playlistPath = await ffmpeg.TranscodeToHlsAsync(inputPath, tierDir, tier, ct);

                // Upload all HLS files (playlist + segments) to S3
                var hlsFiles = Directory.GetFiles(tierDir);
                foreach (var file in hlsFiles)
                {
                    var hlsKey = $"media/{mediaId}/hls/{tier.Name}/{Path.GetFileName(file)}";
                    var hlsMime = file.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                        ? "application/vnd.apple.mpegurl"
                        : "video/mp2t";
                    await UploadFileAsync(file, hlsKey, hlsMime, ct);
                }

                var playlistKey = $"media/{mediaId}/hls/{tier.Name}/{tier.Name}.m3u8";
                variants.Add(new MediaVariant
                {
                    Kind = $"hls-{tier.Name}",
                    S3Key = playlistKey,
                    MimeType = "application/vnd.apple.mpegurl",
                    Size = new FileInfo(playlistPath).Length,
                    Height = tier.Height,
                    DurationMs = probe.DurationSeconds.HasValue
                        ? (int)(probe.DurationSeconds.Value * 1000)
                        : null,
                });
            }

            // Upload master playlist
            if (variants.Count > 0)
            {
                var masterPath = Path.Combine(workDir, "master.m3u8");
                await WriteMasterPlaylistAsync(masterPath, variants, ct);
                var masterKey = $"media/{mediaId}/hls/master.m3u8";
                await UploadFileAsync(masterPath, masterKey, "application/vnd.apple.mpegurl", ct);

                variants.Add(new MediaVariant
                {
                    Kind = "hls-master",
                    S3Key = masterKey,
                    MimeType = "application/vnd.apple.mpegurl",
                    Size = new FileInfo(masterPath).Length,
                });
            }

            return variants;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to cleanup {Dir}", workDir); }
        }
    }

    private async Task DownloadToFileAsync(string s3Key, string filePath, CancellationToken ct)
    {
        await s3.DownloadToFileAsync(s3Key, filePath, ct);
    }

    private async Task UploadFileAsync(string filePath, string s3Key, string mimeType, CancellationToken ct)
    {
        await using var fs = File.OpenRead(filePath);
        await s3.UploadAsync(s3Key, mimeType, fs, ct);
    }

    private static async Task WriteMasterPlaylistAsync(
        string path, IReadOnlyList<MediaVariant> variants, CancellationToken ct)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var v in variants.Where(v => v.Kind.StartsWith("hls-", StringComparison.Ordinal) && v.Kind != "hls-master"))
        {
            var bandwidth = v.Kind switch
            {
                "hls-1080p" => 5_000_000,
                "hls-720p" => 2_500_000,
                "hls-480p" => 1_000_000,
                "hls-360p" => 500_000,
                _ => 1_000_000,
            };
            var tierName = v.Kind.Replace("hls-", "", StringComparison.Ordinal);
            lines.Add($"#EXT-X-STREAM-INF:BANDWIDTH={bandwidth},RESOLUTION=x{v.Height}");
            lines.Add($"{tierName}/{tierName}.m3u8");
        }
        await File.WriteAllLinesAsync(path, lines, ct);
    }
}
