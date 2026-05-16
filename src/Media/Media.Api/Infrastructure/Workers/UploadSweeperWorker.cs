using Haworks.Media.Api.Options;
using Microsoft.Extensions.Options;

namespace Haworks.Media.Api.Infrastructure.Workers;

public sealed class UploadSweeperWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<UploadOptions> opts,
    ILogger<UploadSweeperWorker> logger) : BackgroundService
{
    private readonly UploadOptions _opts = opts.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_opts.SweeperIntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Upload sweeper iteration failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var s3 = scope.ServiceProvider.GetRequiredService<IS3Service>();

        var cutoff = DateTime.UtcNow.AddHours(-_opts.PendingUploadTtlHours);

        // Sweep stale Pending uploads (abandoned) AND Rejected files (malware)
        var stale = await context.MediaFiles
            .Where(f => (f.Status == MediaStatus.Pending || f.Status == MediaStatus.Rejected) && f.CreatedAt < cutoff)
            .Take(100)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        logger.LogInformation("Sweeping {Count} stale/rejected files older than {Cutoff}", stale.Count, cutoff);

        foreach (var file in stale)
        {
            try
            {
                if (file.UploadKind == UploadKind.Multipart && !string.IsNullOrEmpty(file.S3UploadId)
                    && file.Status == MediaStatus.Pending)
                {
                    await s3.AbortMultipartUploadAsync(file.Id.ToString(), file.S3UploadId, ct);
                }

                context.MediaFiles.Remove(file);
                await context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sweep file {MediaId}", file.Id);
            }
        }
    }
}
