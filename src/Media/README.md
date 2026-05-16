# Media Service

Upload, virus-scan, and process media files (images, video, audio) with S3 storage.

## Architecture

```
Client → PUT presigned URL → S3
Client → POST /complete → Media API → Download → Hash verify → ClamAV scan
                                     → Quarantine → Active/Rejected (in outbox tx)
                                     → Publish MediaScanPassedEvent
                                     → Send ProcessMediaCommand (async)

ProcessMediaConsumer → Transcode/Thumbnails/Normalize → MediaProcessingCompletedEvent
ProcessMediaFaultConsumer → MediaProcessingFailedEvent (after retries exhausted)

S3 ObjectCreated → SQS → S3EventConsumer → MediaUploadCompletedEvent
                        → MediaUploadCompletedConsumer (same scan pipeline)
```

## Key Design Decisions

- **Async processing**: Transcoding runs in a MassTransit consumer, never in the HTTP request path
- **Outbox atomicity**: All events published inside the EF transaction via MassTransit outbox
- **Temp file scanning**: Downloads to disk (not memory) — supports files up to 256GB without OOM
- **Single code path**: Both HTTP and S3-event paths feed into the same scan + event pipeline
- **Commands via Send**: `ProcessMediaCommand` uses point-to-point delivery (not fan-out Publish)
- **Fault handling**: `ProcessMediaFaultConsumer` publishes failure event after retries exhausted

## Upload Flow

1. `POST /api/media/initiate` — creates record, returns presigned PUT URL (or multipart URLs)
2. Client uploads directly to S3 via presigned URL
3. `POST /api/media/{id}/complete` — triggers scan pipeline:
   - Download to temp file
   - SHA-256 hash verification (reject on mismatch)
   - ClamAV virus scan (file-path mode for large files)
   - Mark Active or Rejected inside outbox transaction
   - Publish events + send processing command atomically

## Configuration

| Section | Key Fields |
|---|---|
| `Storage` | `Enabled`, `ServiceUrl`, `BucketName`, `AccessKey`, `SecretKey`, `Region` |
| `ClamAV` | `Enabled`, `Host`, `Port`, `TimeoutSeconds`, `InStreamMaxBytes` |
| `Upload` | `SinglePutMaxBytes` (8MB), `MaxFileSizeBytes` (256GB), `AllowedMimeTypes` |
| `Transcode` | `Enabled`, `FfmpegPath`, `MaxConcurrentJobs`, `QualityTiers` |
| `Image` | `Enabled`, `ThumbnailSizes`, `WebPQuality`, `StripExifGps` |
| `S3Notifications` | `Enabled`, `SqsQueueUrl`, `PollIntervalSeconds` |

## Running Tests

```bash
# Unit tests (no Docker required)
dotnet test tests/Media/Media.Unit/

# Integration tests (requires Docker for Postgres + LocalStack)
dotnet test tests/Media/Media.Integration/
```
