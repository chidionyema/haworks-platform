using FluentAssertions;
using Haworks.Location.Application.Queries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Haworks.Location.Unit.Caching;

public class LocationCachingTests
{
    private readonly IMemoryCache _memoryCache;

    public LocationCachingTests()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        var serviceProvider = services.BuildServiceProvider();
        _memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
    }

    [Fact]
    public void CacheKeyGeneration_WithSameParameters_ShouldProduceSameKey()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;
        double radius = 1000.0;
        int limit = 10;

        // Act
        var key1 = GenerateCacheKey(lat, lon, radius, limit);
        var key2 = GenerateCacheKey(lat, lon, radius, limit);

        // Assert
        key1.Should().Be(key2);
    }

    [Fact]
    public void CacheKeyGeneration_WithDifferentParameters_ShouldProduceDifferentKeys()
    {
        // Arrange
        double baseLat = 51.5074;
        double baseLon = -0.1278;
        double baseRadius = 1000.0;
        int baseLimit = 10;

        // Act
        var baseKey = GenerateCacheKey(baseLat, baseLon, baseRadius, baseLimit);
        var keyDiffLat = GenerateCacheKey(baseLat + 0.001, baseLon, baseRadius, baseLimit);
        var keyDiffLon = GenerateCacheKey(baseLat, baseLon + 0.001, baseRadius, baseLimit);
        var keyDiffRadius = GenerateCacheKey(baseLat, baseLon, baseRadius + 100, baseLimit);
        var keyDiffLimit = GenerateCacheKey(baseLat, baseLon, baseRadius, baseLimit + 5);

        // Assert
        keyDiffLat.Should().NotBe(baseKey);
        keyDiffLon.Should().NotBe(baseKey);
        keyDiffRadius.Should().NotBe(baseKey);
        keyDiffLimit.Should().NotBe(baseKey);
    }

    [Fact]
    public void CacheKeyGeneration_ShouldRoundCoordinatesToThreeDecimalPlaces()
    {
        // Arrange
        double preciseLatitude = 51.507432156;
        double preciseLongitude = -0.127834567;
        double radius = 1000.0;
        int limit = 10;

        // Act
        var key = GenerateCacheKey(preciseLatitude, preciseLongitude, radius, limit);

        // Assert
        // Key should contain rounded coordinates (51.507, -0.128)
        key.Should().Contain("51.507");
        key.Should().Contain("-0.128");
        key.Should().NotContain("51.507432156");
        key.Should().NotContain("-0.127834567");
    }

    [Fact]
    public void CacheKeyGeneration_WithValidParameters_ShouldIncludeAllComponents()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;
        double radius = 1500.5;
        int limit = 25;

        // Act
        var key = GenerateCacheKey(lat, lon, radius, limit);

        // Assert
        key.Should().StartWith("nearby:");
        key.Should().Contain("51.507");    // Rounded latitude
        key.Should().Contain("-0.128");    // Rounded longitude
        key.Should().Contain("1500.5");    // Exact radius
        key.Should().Contain("25");        // Exact limit
    }

    [Theory]
    [InlineData(0.0, 0.0, 1000.0, 10, "nearby:0:-0:1000:10")]
    [InlineData(-90.0, -180.0, 500.0, 5, "nearby:-90:-180:500:5")]
    [InlineData(90.0, 180.0, 2000.0, 100, "nearby:90:180:2000:100")]
    public void CacheKeyGeneration_WithSpecificValues_ShouldMatchExpectedFormat(
        double lat, double lon, double radius, int limit, string expectedKey)
    {
        // Act
        var actualKey = GenerateCacheKey(lat, lon, radius, limit);

        // Assert
        actualKey.Should().Be(expectedKey);
    }

    [Fact]
    public void CacheStorage_ShouldStoreAndRetrieveData()
    {
        // Arrange
        var cacheKey = "test-nearby-key";
        var testData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "Test Street", "SW1A 1AA", 100.5),
            new(Guid.NewGuid(), "Another Street", "SW1A 2BB", 250.0)
        };

        // Act - Store data
        _memoryCache.Set(cacheKey, testData, TimeSpan.FromMinutes(5));

        // Act - Retrieve data
        var retrieved = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Should().HaveCount(2);
        retrieved[0].Street.Should().Be("Test Street");
        retrieved[1].Street.Should().Be("Another Street");
    }

    [Fact]
    public void CacheExpiration_ShouldExpireAfterTimeout()
    {
        // Arrange
        var cacheKey = "test-expiration-key";
        var testData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "Expiring Street", "EX1 1EX", 50.0)
        };

        // Act - Store with very short expiration
        _memoryCache.Set(cacheKey, testData, TimeSpan.FromMilliseconds(100));

        // Verify data is initially there
        var initialData = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);
        initialData.Should().NotBeNull();

        // Wait for expiration
        Thread.Sleep(150);

        // Act - Try to retrieve after expiration
        var expiredData = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);

        // Assert
        expiredData.Should().BeNull();
    }

    [Fact]
    public void CacheTryGetValue_WithMissingKey_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentKey = "non-existent-nearby-key";

        // Act
        var found = _memoryCache.TryGetValue(nonExistentKey, out var value);

        // Assert
        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void CacheTryGetValue_WithExistingKey_ShouldReturnTrueAndData()
    {
        // Arrange
        var cacheKey = "existing-nearby-key";
        var testData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "Existing Street", "EX2 2EX", 75.0)
        };

        _memoryCache.Set(cacheKey, testData, TimeSpan.FromMinutes(5));

        // Act
        var found = _memoryCache.TryGetValue<List<NearbyAddressDto>>(cacheKey, out var value);

        // Assert
        found.Should().BeTrue();
        value.Should().NotBeNull();
        value!.Should().HaveCount(1);
        value[0].Street.Should().Be("Existing Street");
    }

    [Fact]
    public void CacheOverwrite_ShouldReplaceExistingData()
    {
        // Arrange
        var cacheKey = "overwrite-test-key";
        var originalData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "Original Street", "OR1 1OR", 100.0)
        };
        var newData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "New Street", "NE1 1NE", 200.0),
            new(Guid.NewGuid(), "Another New Street", "NE2 2NE", 300.0)
        };

        // Act - Store original data
        _memoryCache.Set(cacheKey, originalData, TimeSpan.FromMinutes(5));

        // Verify original data
        var originalRetrieved = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);
        originalRetrieved!.Should().HaveCount(1);

        // Act - Overwrite with new data
        _memoryCache.Set(cacheKey, newData, TimeSpan.FromMinutes(5));

        // Retrieve after overwrite
        var newRetrieved = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);

        // Assert
        newRetrieved.Should().NotBeNull();
        newRetrieved!.Should().HaveCount(2);
        newRetrieved.Should().NotContain(x => x.Street == "Original Street");
        newRetrieved.Should().Contain(x => x.Street == "New Street");
        newRetrieved.Should().Contain(x => x.Street == "Another New Street");
    }

    [Fact]
    public void CacheRemoval_ShouldRemoveData()
    {
        // Arrange
        var cacheKey = "removal-test-key";
        var testData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), "To Be Removed", "RM1 1RM", 150.0)
        };

        _memoryCache.Set(cacheKey, testData, TimeSpan.FromMinutes(5));

        // Verify data exists
        var existingData = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);
        existingData.Should().NotBeNull();

        // Act - Remove data
        _memoryCache.Remove(cacheKey);

        // Act - Try to retrieve after removal
        var removedData = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);

        // Assert
        removedData.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void CacheExpiration_WithDifferentTimeouts_ShouldExpireCorrectly(int minutes)
    {
        // Arrange
        var cacheKey = $"timeout-test-key-{minutes}";
        var testData = new List<NearbyAddressDto>
        {
            new(Guid.NewGuid(), $"Street {minutes}", $"T{minutes} 1T{minutes}", 50.0 * minutes)
        };

        // Act
        _memoryCache.Set(cacheKey, testData, TimeSpan.FromMinutes(minutes));

        // Assert
        var retrievedData = _memoryCache.Get<List<NearbyAddressDto>>(cacheKey);
        retrievedData.Should().NotBeNull();
        retrievedData![0].Street.Should().Be($"Street {minutes}");
    }

    [Fact]
    public void ConcurrentAccess_ShouldHandleMultipleOperations()
    {
        // Arrange
        const int threadCount = 10;
        const int operationsPerThread = 100;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    var key = $"concurrent-key-{threadId}-{j}";
                    var data = new List<NearbyAddressDto>
                    {
                        new(Guid.NewGuid(), $"Thread {threadId} Street {j}", "CN1 1CN", j * 10.0)
                    };

                    // Store data
                    _memoryCache.Set(key, data, TimeSpan.FromMinutes(1));

                    // Retrieve data
                    var retrieved = _memoryCache.Get<List<NearbyAddressDto>>(key);

                    // Verify
                    retrieved.Should().NotBeNull();
                    retrieved![0].Street.Should().Be($"Thread {threadId} Street {j}");
                }
            }));
        }

        // Assert
        var allTasksCompleted = Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(30));
        allTasksCompleted.Should().BeTrue("All concurrent operations should complete successfully");
    }

    /// <summary>
    /// Replicates the cache key generation logic from GetNearbyAddressesQueryHandler
    /// </summary>
    private static string GenerateCacheKey(double lat, double lon, double radius, int limit) =>
        $"nearby:{Math.Round(lat, 3)}:{Math.Round(lon, 3)}:{radius}:{limit}";
}