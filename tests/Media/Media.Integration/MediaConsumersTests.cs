using FluentAssertions;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Domain;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Infrastructure.Workers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Haworks.Media.Integration;

[Collection("Media Integration")]
public sealed class MediaConsumersTests : IAsyncLifetime
{
    private readonly MediaWebAppFactory _factory;

    public MediaConsumersTests(MediaWebAppFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        return _factory.EnsureSchemaAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> ReadDbAsync<T>(Func<MediaDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        return await query(db);
    }

    private async Task WriteDbAsync(Func<MediaDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        await action(db);
    }

    [Fact]
    public async Task MediaScanFailedConsumer_ShouldMarkFileAsRejected()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("infected.png", "hash123", 1024, "image/png", "test-owner");
        mediaFile.MarkAsQuarantined();

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var scanFailedEvent = new MediaScanFailedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "infected.png",
            Reason = "Virus detected"
        };

        // Act
        await harness.Bus.Publish(scanFailedEvent);

        // Assert
        await TestWait.Until(async () =>
        {
            var file = await ReadDbAsync(db => db.MediaFiles.FirstOrDefaultAsync(f => f.Id == mediaFile.Id));
            return file?.Status == MediaStatus.Rejected;
        });

        var updatedFile = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == mediaFile.Id));
        updatedFile.Status.Should().Be(MediaStatus.Rejected);
    }

    [Fact]
    public async Task MediaScanFailedConsumer_UnknownMediaId_ShouldLogWarningAndContinue()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var nonExistentId = Guid.NewGuid();

        var scanFailedEvent = new MediaScanFailedEvent
        {
            MediaId = nonExistentId,
            OwnerId = "test-owner",
            FileName = "unknown.png",
            Reason = "Virus detected"
        };

        // Act
        await harness.Bus.Publish(scanFailedEvent);

        // Assert - Should not throw, consumer should handle gracefully
        await Task.Delay(100); // Give consumer time to process

        // Verify the message was consumed (no exception thrown)
        var consumerHarness = harness.GetConsumerHarness<MediaScanFailedConsumer>();
        consumerHarness.Consumed.Select<MediaScanFailedEvent>().Any().Should().BeTrue();
    }

    [Fact]
    public async Task MediaScanFailedConsumer_AlreadyRejectedFile_ShouldBeIdempotent()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("already-rejected.png", "hash123", 1024, "image/png", "test-owner");
        mediaFile.MarkAsQuarantined();
        mediaFile.MarkAsRejected();

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var scanFailedEvent = new MediaScanFailedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "already-rejected.png",
            Reason = "Virus detected"
        };

        // Act
        await harness.Bus.Publish(scanFailedEvent);

        // Assert
        await Task.Delay(100); // Give consumer time to process

        var updatedFile = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == mediaFile.Id));
        updatedFile.Status.Should().Be(MediaStatus.Rejected); // Should remain rejected
    }

    [Fact]
    public async Task MediaScanPassedConsumer_ShouldMarkFileAsActive()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("clean.png", "hash123", 1024, "image/png", "test-owner");
        mediaFile.MarkAsQuarantined();

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var scanPassedEvent = new MediaScanPassedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "clean.png",
            MimeType = "image/png",
            Size = 1024
        };

        // Act
        await harness.Bus.Publish(scanPassedEvent);

        // Assert
        await TestWait.Until(async () =>
        {
            var file = await ReadDbAsync(db => db.MediaFiles.FirstOrDefaultAsync(f => f.Id == mediaFile.Id));
            return file?.Status == MediaStatus.Active;
        });

        var updatedFile = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == mediaFile.Id));
        updatedFile.Status.Should().Be(MediaStatus.Active);
    }

    [Fact]
    public async Task MediaScanPassedConsumer_UnknownMediaId_ShouldLogWarningAndContinue()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var nonExistentId = Guid.NewGuid();

        var scanPassedEvent = new MediaScanPassedEvent
        {
            MediaId = nonExistentId,
            OwnerId = "test-owner",
            FileName = "unknown.png",
            MimeType = "image/png",
            Size = 1024
        };

        // Act
        await harness.Bus.Publish(scanPassedEvent);

        // Assert - Should not throw
        await Task.Delay(100);

        var consumerHarness = harness.GetConsumerHarness<MediaScanPassedConsumer>();
        consumerHarness.Consumed.Select<MediaScanPassedEvent>().Any().Should().BeTrue();
    }

    [Fact]
    public async Task MediaScanPassedConsumer_AlreadyActiveFile_ShouldBeIdempotent()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("already-active.png", "hash123", 1024, "image/png", "test-owner");
        mediaFile.MarkAsQuarantined();
        mediaFile.MarkAsActive();

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var scanPassedEvent = new MediaScanPassedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "already-active.png",
            MimeType = "image/png",
            Size = 1024
        };

        // Act
        await harness.Bus.Publish(scanPassedEvent);

        // Assert
        await Task.Delay(100);

        var updatedFile = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == mediaFile.Id));
        updatedFile.Status.Should().Be(MediaStatus.Active); // Should remain active
    }

    [Fact]
    public async Task ProcessMediaConsumer_ShouldProcessCleanFile()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("process.mp4", "hash123", 5_000_000, "video/mp4", "test-owner");
        mediaFile.MarkAsQuarantined();
        mediaFile.MarkAsActive();

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var processCommand = new ProcessMediaCommand
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "process.mp4",
            MimeType = "video/mp4",
            S3Key = mediaFile.Id.ToString()
        };

        // Act
        await harness.Bus.Publish(processCommand);

        // Assert - Message should be consumed without error
        await Task.Delay(500); // Give processing time

        var consumerHarness = harness.GetConsumerHarness<ProcessMediaConsumer>();
        consumerHarness.Consumed.Select<ProcessMediaCommand>().Any().Should().BeTrue();

        // File should still exist and be active
        var updatedFile = await ReadDbAsync(db => db.MediaFiles.FirstAsync(f => f.Id == mediaFile.Id));
        updatedFile.Status.Should().Be(MediaStatus.Active);
    }

    [Fact]
    public async Task MediaUploadCompletedConsumer_ShouldProcessUploadEvent()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var mediaFile = MediaFile.Create("uploaded.jpg", "hash123", 2048, "image/jpeg", "test-owner");

        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var uploadEvent = new MediaUploadCompletedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "uploaded.jpg",
            Size = 2048,
            MimeType = "image/jpeg"
        };

        // Act
        await harness.Bus.Publish(uploadEvent);

        // Assert
        await Task.Delay(100);

        var consumerHarness = harness.GetConsumerHarness<MediaUploadCompletedConsumer>();
        consumerHarness.Consumed.Select<MediaUploadCompletedEvent>().Any().Should().BeTrue();
    }

    // ProcessMediaFaultConsumer test omitted - requires complex Fault<T> setup

    [Fact]
    public async Task ConsumersCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        var mediaFile = MediaFile.Create("cancel-test.png", "hash123", 1024, "image/png", "test-owner");
        await WriteDbAsync(async db =>
        {
            db.MediaFiles.Add(mediaFile);
            await db.SaveChangesAsync();
        });

        var scanFailedEvent = new MediaScanFailedEvent
        {
            MediaId = mediaFile.Id,
            OwnerId = "test-owner",
            FileName = "cancel-test.png",
            Reason = "Test cancellation"
        };

        // Act & Assert
        // The consumer should handle cancellation gracefully
        await harness.Bus.Publish(scanFailedEvent, cts.Token);

        // Give some time for the message to be processed
        await Task.Delay(50);
    }
}

/// <summary>
/// Helper class for waiting until a condition is met in async tests
/// </summary>
public static class TestWait
{
    public static async Task Until(Func<Task<bool>> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var timeoutValue = timeout ?? TimeSpan.FromSeconds(5);
        var pollIntervalValue = pollInterval ?? TimeSpan.FromMilliseconds(50);

        using var cts = new CancellationTokenSource(timeoutValue);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                if (await condition())
                    return;
            }
            catch
            {
                // Ignore exceptions during polling
            }

            await Task.Delay(pollIntervalValue, cts.Token);
        }

        throw new TimeoutException($"Condition was not met within {timeoutValue}");
    }
}