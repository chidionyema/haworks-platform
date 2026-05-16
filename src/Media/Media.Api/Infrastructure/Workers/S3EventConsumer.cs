using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Haworks.Media.Api.Options;
using Microsoft.Extensions.Options;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// Background worker that polls an SQS queue for S3 ObjectCreated events.
/// When a file is uploaded to S3, this worker detects it and triggers the virus scan pipeline,
/// eliminating the need for the client to call POST /complete.
/// </summary>
public sealed class S3EventConsumer(
    IServiceScopeFactory scopeFactory,
    IAmazonSQS sqs,
    IOptions<S3NotificationOptions> opts,
    ILogger<S3EventConsumer> logger) : BackgroundService
{
    private readonly S3NotificationOptions _opts = opts.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_opts.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "S3 event consumer iteration failed");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _opts.SqsQueueUrl,
            MaxNumberOfMessages = _opts.MaxMessages,
            WaitTimeSeconds = 5,
            VisibilityTimeout = _opts.VisibilityTimeoutSeconds,
        }, ct);

        if (response.Messages.Count == 0) return;

        foreach (var message in response.Messages)
        {
            try
            {
                await HandleMessageAsync(message, ct);
                await sqs.DeleteMessageAsync(_opts.SqsQueueUrl, message.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process S3 event message {MessageId}", message.MessageId);
                // Message will become visible again after visibility timeout expires
            }
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        // S3 event notification JSON structure
        using var doc = JsonDocument.Parse(message.Body);

        // Handle SNS-wrapped messages (S3 → SNS → SQS)
        JsonElement records;
        if (doc.RootElement.TryGetProperty("Records", out records))
        {
            // Direct S3 → SQS
        }
        else if (doc.RootElement.TryGetProperty("Message", out var snsMessage))
        {
            // SNS → SQS wrapper
            using var inner = JsonDocument.Parse(snsMessage.GetString()!);
            records = inner.RootElement.GetProperty("Records");
        }
        else
        {
            logger.LogWarning("Unrecognized S3 event format: {Body}", message.Body[..Math.Min(500, message.Body.Length)]);
            return;
        }

        foreach (var record in records.EnumerateArray())
        {
            var eventName = record.GetProperty("eventName").GetString();
            if (eventName == null || !eventName.StartsWith("ObjectCreated:", StringComparison.Ordinal))
                continue;

            var s3Key = record.GetProperty("s3").GetProperty("object").GetProperty("key").GetString();
            if (string.IsNullOrEmpty(s3Key)) continue;

            // s3Key is the MediaFile.Id (GUID)
            if (!Guid.TryParse(s3Key, out var mediaId))
            {
                logger.LogDebug("Skipping S3 event for non-media key: {Key}", s3Key);
                continue;
            }

            await TriggerScanAsync(mediaId, ct);
        }
    }

    private async Task TriggerScanAsync(Guid mediaId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var file = await context.MediaFiles.FirstOrDefaultAsync(f => f.Id == mediaId, ct);
        if (file == null)
        {
            logger.LogDebug("S3 event for unknown media {MediaId} — skipping", mediaId);
            return;
        }

        if (file.Status != MediaStatus.Pending)
        {
            logger.LogDebug("S3 event for already-processed media {MediaId} (status={Status}) — skipping", mediaId, file.Status);
            return;
        }

        // Trigger scan via MediatR
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        // Use a system-level current user since this is server-initiated
        var scanner = scope.ServiceProvider.GetRequiredService<IVirusScanner>();
        var s3 = scope.ServiceProvider.GetRequiredService<IS3Service>();

        await using var tx = await context.Database.BeginTransactionAsync(ct);
        try
        {
            file.MarkAsQuarantined();
            await context.SaveChangesAsync(ct);

            await using var stream = await s3.DownloadAsync(file.Id.ToString(), ct);
            var isClean = await scanner.ScanAsync(stream, ct);

            if (isClean)
                file.MarkAsActive();
            else
                file.MarkAsRejected();

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("S3 event scan complete for {MediaId}: {Status}", mediaId, file.Status);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
