using FluentAssertions;
using Xunit;
using Haworks.Notifications.Domain.Entities;

namespace Haworks.Notifications.Unit.Domain;

[Trait("Category", "Unit")]
public sealed class RateLimitBucketTests
{
    [Fact]
    public void Create_ValidParameters_CreatesRateLimitBucketWithCorrectProperties()
    {
        // Arrange
        var bucketKey = "user:123:email";
        var windowStart = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Utc);
        var count = 5;

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart, count);

        // Assert
        bucket.Should().NotBeNull();
        bucket.Id.Should().NotBe(Guid.Empty);
        bucket.BucketKey.Should().Be(bucketKey);
        bucket.WindowStart.Should().Be(windowStart);
        bucket.Count.Should().Be(count);
    }

    [Fact]
    public void Create_DefaultCount_CreatesRateLimitBucketWithZeroCount()
    {
        // Arrange
        var bucketKey = "user:456:sms";
        var windowStart = new DateTime(2026, 5, 31, 11, 0, 0, DateTimeKind.Utc);

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart);

        // Assert
        bucket.Should().NotBeNull();
        bucket.Id.Should().NotBe(Guid.Empty);
        bucket.BucketKey.Should().Be(bucketKey);
        bucket.WindowStart.Should().Be(windowStart);
        bucket.Count.Should().Be(0);
    }

    [Fact]
    public void Create_GeneratesUniqueIds()
    {
        // Arrange
        var bucketKey = "test-key";
        var windowStart = DateTime.UtcNow;

        // Act
        var bucket1 = RateLimitBucket.Create(bucketKey, windowStart);
        var bucket2 = RateLimitBucket.Create(bucketKey, windowStart);

        // Assert
        bucket1.Id.Should().NotBe(bucket2.Id);
        bucket1.Id.Should().NotBe(Guid.Empty);
        bucket2.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("user:123:email:hourly")]
    [InlineData("ip:192.168.1.1:global")]
    [InlineData("service:notifications:api:daily")]
    [InlineData("")]
    public void Create_DifferentBucketKeys_PreservesKeyValues(string bucketKey)
    {
        // Arrange
        var windowStart = DateTime.UtcNow;

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart);

        // Assert
        bucket.BucketKey.Should().Be(bucketKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Create_DifferentCounts_PreservesCountValues(int count)
    {
        // Arrange
        var bucketKey = "test-bucket";
        var windowStart = DateTime.UtcNow;

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart, count);

        // Assert
        bucket.Count.Should().Be(count);
    }

    [Fact]
    public void Create_PastWindowStart_PreservesDateValue()
    {
        // Arrange
        var bucketKey = "historical-bucket";
        var windowStart = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart);

        // Assert
        bucket.WindowStart.Should().Be(windowStart);
    }

    [Fact]
    public void Create_FutureWindowStart_PreservesDateValue()
    {
        // Arrange
        var bucketKey = "future-bucket";
        var windowStart = new DateTime(2030, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var bucket = RateLimitBucket.Create(bucketKey, windowStart);

        // Assert
        bucket.WindowStart.Should().Be(windowStart);
    }

    [Fact]
    public void BucketKey_HasPrivateSetter()
    {
        // Arrange
        var bucket = RateLimitBucket.Create("test-key", DateTime.UtcNow);

        // Act & Assert - Verify BucketKey cannot be set externally
        var property = typeof(RateLimitBucket).GetProperty(nameof(RateLimitBucket.BucketKey));
        property.Should().NotBeNull();
        property!.SetMethod.Should().NotBeNull("private setter should exist");
        property.SetMethod!.IsPrivate.Should().BeTrue("setter should be private");
    }

    [Fact]
    public void WindowStart_HasPrivateSetter()
    {
        // Arrange
        var bucket = RateLimitBucket.Create("test-key", DateTime.UtcNow);

        // Act & Assert - Verify WindowStart cannot be set externally
        var property = typeof(RateLimitBucket).GetProperty(nameof(RateLimitBucket.WindowStart));
        property.Should().NotBeNull();
        property!.SetMethod.Should().NotBeNull("private setter should exist");
        property.SetMethod!.IsPrivate.Should().BeTrue("setter should be private");
    }

    [Fact]
    public void Count_HasPrivateSetter()
    {
        // Arrange
        var bucket = RateLimitBucket.Create("test-key", DateTime.UtcNow);

        // Act & Assert - Verify Count cannot be set externally
        var property = typeof(RateLimitBucket).GetProperty(nameof(RateLimitBucket.Count));
        property.Should().NotBeNull();
        property!.SetMethod.Should().NotBeNull("private setter should exist");
        property.SetMethod!.IsPrivate.Should().BeTrue("setter should be private");
    }
}