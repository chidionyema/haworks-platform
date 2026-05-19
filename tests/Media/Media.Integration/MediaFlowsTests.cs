using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Application;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Media.Integration;

[Collection("Media Integration")]
public sealed class MediaFlowsTests : IAsyncLifetime
{
    private readonly MediaWebAppFactory _factory;
    private readonly HttpClient _client;

    public MediaFlowsTests(MediaWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        return _factory.EnsureSchemaAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Helpers ───

    private static (byte[] bytes, string hash) GenerateTestFile(int size = 1024)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, hash);
    }

    private async Task<Guid> UploadFileToS3(Guid mediaId, byte[] content, string mimeType)
    {
        // Upload directly to LocalStack (simulating the client PUT to presigned URL)
        var s3Config = new AmazonS3Config
        {
            ServiceURL = _factory.LocalstackUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        };
        using var s3 = new AmazonS3Client("test", "test", s3Config);
        await using var stream = new MemoryStream(content);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = MediaWebAppFactory.Bucket,
            Key = mediaId.ToString(),
            ContentType = mimeType,
            InputStream = stream,
        });
        return mediaId;
    }

    private async Task<T> ReadDbAsync<T>(Func<MediaDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        return await query(db);
    }

    // ─── Tests ───

    [Fact]
    public async Task InitiateUpload_ReturnsPresignedUrl()
    {
        var (_, hash) = GenerateTestFile();
        var response = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "test.png",
            Hash = hash,
            Size = 1024L,
            MimeType = "image/png",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.UploadUrl.Should().NotBeNullOrEmpty();
        body.AlreadyExists.Should().BeFalse();
        body.IsMultipart.Should().BeFalse();

        // Verify DB record exists in Pending state
        var file = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == body.Id));
        file.Status.Should().Be(MediaStatus.Pending);
        file.FileName.Should().Be("test.png");
    }

    [Fact]
    public async Task InitiateUpload_DuplicateHash_ReturnsSameId()
    {
        var (_, hash) = GenerateTestFile();

        // First upload
        var r1 = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "first.png",
            Hash = hash,
            Size = 1024L,
            MimeType = "image/png",
        });
        var body1 = await r1.Content.ReadFromJsonAsync<UploadResponse>();

        // Second upload with same hash
        var r2 = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "second.png",
            Hash = hash,
            Size = 1024L,
            MimeType = "image/png",
        });
        var body2 = await r2.Content.ReadFromJsonAsync<UploadResponse>();

        body2!.Id.Should().Be(body1!.Id);
        body2.AlreadyExists.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteUpload_CleanFile_TransitionsToActive()
    {
        var (bytes, hash) = GenerateTestFile();

        // 1. Initiate
        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "clean.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();

        // 2. Upload to S3
        await UploadFileToS3(upload!.Id, bytes, "image/png");

        // 3. Complete (triggers virus scan)
        var completeResponse = await _client.PostAsync($"/api/media/{upload.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verify DB state is Active
        var file = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == upload.Id));
        file.Status.Should().Be(MediaStatus.Active);
    }

    [Fact]
    public async Task CompleteUpload_InfectedFile_TransitionsToRejected()
    {
        _factory.VirusScanShouldFail = true;
        try
        {
            var (bytes, hash) = GenerateTestFile();

            var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
            {
                FileName = "infected.exe",
                Hash = hash,
                Size = (long)bytes.Length,
                MimeType = "application/octet-stream",
            });
            var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();
            await UploadFileToS3(upload!.Id, bytes, "application/octet-stream");

            var completeResponse = await _client.PostAsync($"/api/media/{upload.Id}/complete", null);
            completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var file = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == upload.Id));
            file.Status.Should().Be(MediaStatus.Rejected);
        }
        finally
        {
            _factory.VirusScanShouldFail = false;
        }
    }

    [Fact]
    public async Task CompleteUpload_HashMismatch_TransitionsToRejected()
    {
        var (bytes, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "mismatch.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();

        // Upload DIFFERENT bytes to S3 (hash won't match)
        var differentBytes = new byte[bytes.Length];
        Random.Shared.NextBytes(differentBytes);
        await UploadFileToS3(upload!.Id, differentBytes, "image/png");

        var completeResponse = await _client.PostAsync($"/api/media/{upload.Id}/complete", null);
        // Hash mismatch returns a failure result
        completeResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        var file = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == upload.Id));
        file.Status.Should().Be(MediaStatus.Rejected);
    }

    [Fact]
    public async Task GetMedia_ReturnsMetadata()
    {
        var (bytes, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "meta.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();

        var getResponse = await _client.GetAsync($"/api/media/{upload!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadFromJsonAsync<MediaFileResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(upload.Id);
        body.FileName.Should().Be("meta.png");
        body.MimeType.Should().Be("image/png");
        body.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task GetMedia_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/media/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMedia_ReturnsPaginatedResults()
    {
        // Create a few files
        for (int i = 0; i < 3; i++)
        {
            var (_, hash) = GenerateTestFile();
            await _client.PostAsJsonAsync("/api/media/initiate", new
            {
                FileName = $"list-{i}.png",
                Hash = hash,
                Size = 1024L,
                MimeType = "image/png",
            });
        }

        var response = await _client.GetAsync("/api/media?page=1&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AbortUpload_RemovesRecord()
    {
        var (_, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "abort.png",
            Hash = hash,
            Size = 1024L,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();

        var abortResponse = await _client.PostAsync($"/api/media/{upload!.Id}/abort", null);
        abortResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var file = await ReadDbAsync(db => db.MediaFiles.FirstOrDefaultAsync(f => f.Id == upload.Id));
        file.Should().BeNull();
    }

    [Fact]
    public async Task GetMediaUrl_ActiveFile_ReturnsPresignedUrl()
    {
        var (bytes, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "url-test.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();
        await UploadFileToS3(upload!.Id, bytes, "image/png");
        await _client.PostAsync($"/api/media/{upload.Id}/complete", null);

        var urlResponse = await _client.GetAsync($"/api/media/{upload.Id}/url");
        urlResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompleteUpload_CleanFile_PublishesScanPassedEvent()
    {
        var (bytes, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "event.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();
        await UploadFileToS3(upload!.Id, bytes, "image/png");
        await _client.PostAsync($"/api/media/{upload.Id}/complete", null);

        // Verify MassTransit published the scan-passed event
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var published = await PollUntilAsync(
            () => harness.Published.Select<MediaScanPassedEvent>()
                .Any(p => p.Context.Message.MediaId == upload.Id),
            TimeSpan.FromSeconds(10));
        published.Should().BeTrue("MediaScanPassedEvent should be published for clean files");
    }

    [Fact]
    public async Task CompleteUpload_CleanFile_SendsProcessMediaCommand()
    {
        var (bytes, hash) = GenerateTestFile();

        var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "process.png",
            Hash = hash,
            Size = (long)bytes.Length,
            MimeType = "image/png",
        });
        var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();
        await UploadFileToS3(upload!.Id, bytes, "image/png");
        await _client.PostAsync($"/api/media/{upload.Id}/complete", null);

        // Verify MassTransit sent the command point-to-point (Send, not Publish)
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var sent = await PollUntilAsync(
            () => harness.Sent.Select<ProcessMediaCommand>()
                .Any(p => p.Context.Message.MediaId == upload.Id),
            TimeSpan.FromSeconds(10));
        sent.Should().BeTrue("ProcessMediaCommand should be sent for async processing");
    }

    [Fact]
    public async Task CompleteUpload_InfectedFile_PublishesScanFailedEvent()
    {
        _factory.VirusScanShouldFail = true;
        try
        {
            var (bytes, hash) = GenerateTestFile();

            var initResponse = await _client.PostAsJsonAsync("/api/media/initiate", new
            {
                FileName = "event-fail.exe",
                Hash = hash,
                Size = (long)bytes.Length,
                MimeType = "application/octet-stream",
            });
            var upload = await initResponse.Content.ReadFromJsonAsync<UploadResponse>();
            await UploadFileToS3(upload!.Id, bytes, "application/octet-stream");
            await _client.PostAsync($"/api/media/{upload.Id}/complete", null);

            var harness = _factory.Services.GetRequiredService<ITestHarness>();
            var published = await PollUntilAsync(
                () => harness.Published.Select<MediaScanFailedEvent>()
                    .Any(p => p.Context.Message.MediaId == upload.Id),
                TimeSpan.FromSeconds(10));
            published.Should().BeTrue("MediaScanFailedEvent should be published for infected files");
        }
        finally
        {
            _factory.VirusScanShouldFail = false;
        }
    }

    [Fact]
    public async Task MultipartUpload_InitiatesWithPartUrls()
    {
        var (_, hash) = GenerateTestFile(50_000_000); // 50MB > 8MB threshold

        var response = await _client.PostAsJsonAsync("/api/media/initiate", new
        {
            FileName = "big.mp4",
            Hash = hash,
            Size = 50_000_000L,
            MimeType = "video/mp4",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        body!.IsMultipart.Should().BeTrue();
        body.S3UploadId.Should().NotBeNullOrEmpty();
        body.PartCount.Should().BeGreaterThan(1);
        body.PartUrls.Should().NotBeNullOrEmpty();
        body.PartUrls!.Count.Should().Be(body.PartCount);

        // Verify DB record
        var file = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == body.Id));
        file.UploadKind.Should().Be(UploadKind.Multipart);
        file.S3UploadId.Should().NotBeNullOrEmpty();
    }

    // ─── Polling helper ───

    private static async Task<bool> PollUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(250);
        }
        return false;
    }
}

/// <summary>
/// Minimal DTO for deserializing controller responses.
/// </summary>
file record MediaFileResponse(
    Guid Id,
    string FileName,
    string MimeType,
    long Size,
    string Status,
    DateTime CreatedAt);
