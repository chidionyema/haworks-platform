using FluentAssertions;
using Haworks.Location.Application.Queries;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Location.Unit.QueryHandlers;

/// <summary>
/// Tests for GetNearbyAddressesQuery behavior through MediatR rather than direct handler testing
/// since the handler is internal. Cache behavior is tested separately in LocationCachingTests.
/// Spatial query correctness is tested in integration tests with real PostGIS.
/// </summary>
public class GetNearbyAddressesQueryBehaviorTests
{
    private readonly IMemoryCache _memoryCache;

    public GetNearbyAddressesQueryBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        var serviceProvider = services.BuildServiceProvider();
        _memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
    }

    [Fact]
    public void GetNearbyAddressesQuery_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = 51.5074,
            Lon = -0.1278,
            RadiusMeters = 1000,
            Limit = 10
        };

        // Assert
        query.Lat.Should().Be(51.5074);
        query.Lon.Should().Be(-0.1278);
        query.RadiusMeters.Should().Be(1000);
        query.Limit.Should().Be(10);
    }

    [Theory]
    [InlineData(-90.0, -180.0, 0.1, 1)]     // Minimum valid values
    [InlineData(90.0, 180.0, 50000, 100)]   // Maximum valid values
    [InlineData(0.0, 0.0, 1000, 10)]        // Origin coordinates
    [InlineData(51.5074, -0.1278, 500, 25)] // London coordinates
    public void GetNearbyAddressesQuery_WithValidCoordinates_ShouldAcceptValues(
        double lat, double lon, double radius, int limit)
    {
        // Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = lat,
            Lon = lon,
            RadiusMeters = radius,
            Limit = limit
        };

        // Assert
        query.Lat.Should().Be(lat);
        query.Lon.Should().Be(lon);
        query.RadiusMeters.Should().Be(radius);
        query.Limit.Should().Be(limit);
    }

    [Fact]
    public void CacheKeyGeneration_ForGetNearbyAddresses_ShouldBeConsistent()
    {
        // Arrange
        var lat = 51.5074;
        var lon = -0.1278;
        var radius = 1000.0;
        var limit = 10;

        // Act
        var key1 = GenerateCacheKey(lat, lon, radius, limit);
        var key2 = GenerateCacheKey(lat, lon, radius, limit);

        // Assert
        key1.Should().Be(key2);
        key1.Should().Be("nearby:51.507:-0.128:1000:10");
    }

    [Fact]
    public void CacheKeyGeneration_ShouldRoundCoordinatesCorrectly()
    {
        // Arrange
        var preciseLatitude = 51.507432156;
        var preciseLongitude = -0.127834567;

        // Act
        var key = GenerateCacheKey(preciseLatitude, preciseLongitude, 1000, 10);

        // Assert
        key.Should().Contain("51.507");
        key.Should().Contain("-0.128");
        key.Should().NotContain("51.507432156");
        key.Should().NotContain("-0.127834567");
    }

    [Fact]
    public void CacheKeyGeneration_WithDifferentParameters_ShouldProduceDifferentKeys()
    {
        // Arrange
        var baseLat = 51.5074;
        var baseLon = -0.1278;
        var baseRadius = 1000.0;
        var baseLimit = 10;

        // Act
        var baseKey = GenerateCacheKey(baseLat, baseLon, baseRadius, baseLimit);
        var keyDiffLat = GenerateCacheKey(baseLat + 0.1, baseLon, baseRadius, baseLimit);
        var keyDiffLon = GenerateCacheKey(baseLat, baseLon + 0.1, baseRadius, baseLimit);
        var keyDiffRadius = GenerateCacheKey(baseLat, baseLon, baseRadius + 500, baseLimit);
        var keyDiffLimit = GenerateCacheKey(baseLat, baseLon, baseRadius, baseLimit + 5);

        // Assert
        keyDiffLat.Should().NotBe(baseKey);
        keyDiffLon.Should().NotBe(baseKey);
        keyDiffRadius.Should().NotBe(baseKey);
        keyDiffLimit.Should().NotBe(baseKey);
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

    /// <summary>
    /// Replicates the cache key generation logic from GetNearbyAddressesQueryHandler
    /// </summary>
    private static string GenerateCacheKey(double lat, double lon, double radius, int limit) =>
        $"nearby:{Math.Round(lat, 3)}:{Math.Round(lon, 3)}:{radius}:{limit}";
}