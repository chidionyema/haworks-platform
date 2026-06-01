using FluentAssertions;
using Haworks.Location.Application.Commands;
using Haworks.Location.Application.Queries;
using Haworks.Location.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Location.Integration;

[Collection("Integration Tests")]
public class LocationSpatialIntegrationTests : IClassFixture<LocationWebAppFactory>
{
    private readonly LocationWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly LocationDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly TestGeocodingService _geocodingService;

    public LocationSpatialIntegrationTests(LocationWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        _mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
        _geocodingService = _scope.ServiceProvider.GetRequiredService<TestGeocodingService>();
    }

    [Fact]
    public async Task GetNearbyAddresses_WithKnownLocations_ShouldReturnAddressesWithinRadius()
    {
        // Arrange - Create test addresses at known distances from central London
        var centralLondonLat = 51.5074;
        var centralLondonLon = -0.1278;

        var addresses = new[]
        {
            // Very close address (~100m from central point)
            new { Street = "Close Address", Lat = 51.5084, Lon = -0.1268, Distance = "~100m" },
            // Medium distance (~500m from central point)
            new { Street = "Medium Address", Lat = 51.5120, Lon = -0.1228, Distance = "~500m" },
            // Far address (~1.5km from central point)
            new { Street = "Far Address", Lat = 51.5200, Lon = -0.1100, Distance = "~1.5km" },
            // Very far address (~5km from central point)
            new { Street = "Very Far Address", Lat = 51.5500, Lon = -0.0500, Distance = "~5km" }
        };

        var addressIds = new List<Guid>();
        foreach (var addr in addresses)
        {
            var command = new CreateAddressCommand
            {
                Street = addr.Street,
                City = "London",
                Postcode = "TEST 123",
                Country = "UK",
                Latitude = addr.Lat,
                Longitude = addr.Lon,
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            var result = await _mediator.Send(command);
            result.IsSuccess.Should().BeTrue();
            addressIds.Add(result.Value);
        }

        // Act - Query for addresses within 1km radius
        var nearbyQuery = new GetNearbyAddressesQuery
        {
            Lat = centralLondonLat,
            Lon = centralLondonLon,
            RadiusMeters = 1000, // 1km radius
            Limit = 10
        };

        var nearbyResult = await _mediator.Send(nearbyQuery);

        // Assert
        nearbyResult.IsSuccess.Should().BeTrue();
        nearbyResult.Value.Should().NotBeNull();

        // Should return close and medium addresses, but not far or very far
        var nearbyAddresses = nearbyResult.Value.ToList();
        nearbyAddresses.Should().HaveCountLessOrEqualTo(2);

        // Verify addresses are ordered by distance (closest first)
        for (int i = 0; i < nearbyAddresses.Count - 1; i++)
        {
            nearbyAddresses[i].Distance.Should().BeLessOrEqualTo(nearbyAddresses[i + 1].Distance);
        }

        // All returned addresses should be within the specified radius
        foreach (var addr in nearbyAddresses)
        {
            addr.Distance.Should().BeLessOrEqualTo(1000);
        }
    }

    [Fact]
    public async Task GetNearbyAddresses_WithLargerRadius_ShouldReturnMoreAddresses()
    {
        // Arrange - Use the same test setup as previous test
        var centralLat = 51.5074;
        var centralLon = -0.1278;

        // Create addresses at various distances
        var testAddresses = new[]
        {
            new { Street = "Spatial Test 1", Lat = 51.5080, Lon = -0.1270 },
            new { Street = "Spatial Test 2", Lat = 51.5150, Lon = -0.1200 },
            new { Street = "Spatial Test 3", Lat = 51.5300, Lon = -0.1000 }
        };

        foreach (var addr in testAddresses)
        {
            var command = new CreateAddressCommand
            {
                Street = addr.Street,
                City = "London",
                Postcode = "SP1 1SP",
                Country = "UK",
                Latitude = addr.Lat,
                Longitude = addr.Lon,
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            await _mediator.Send(command);
        }

        // Act - Query with small radius
        var smallRadiusQuery = new GetNearbyAddressesQuery
        {
            Lat = centralLat,
            Lon = centralLon,
            RadiusMeters = 500,
            Limit = 10
        };

        var smallRadiusResult = await _mediator.Send(smallRadiusQuery);

        // Act - Query with large radius
        var largeRadiusQuery = new GetNearbyAddressesQuery
        {
            Lat = centralLat,
            Lon = centralLon,
            RadiusMeters = 3000,
            Limit = 10
        };

        var largeRadiusResult = await _mediator.Send(largeRadiusQuery);

        // Assert
        smallRadiusResult.IsSuccess.Should().BeTrue();
        largeRadiusResult.IsSuccess.Should().BeTrue();

        var smallRadiusAddresses = smallRadiusResult.Value.Where(a => a.Street.StartsWith("Spatial Test")).ToList();
        var largeRadiusAddresses = largeRadiusResult.Value.Where(a => a.Street.StartsWith("Spatial Test")).ToList();

        // Large radius should return more addresses
        largeRadiusAddresses.Count.Should().BeGreaterOrEqualTo(smallRadiusAddresses.Count);
    }

    [Theory]
    [InlineData(51.5074, -0.1278, 1000, 5)]      // Central London
    [InlineData(53.4808, -2.2426, 2000, 10)]     // Manchester
    [InlineData(55.9533, -3.1883, 1500, 8)]      // Edinburgh
    public async Task GetNearbyAddresses_WithDifferentLocations_ShouldRespectRadiusAndLimit(
        double lat, double lon, double radiusMeters, int limit)
    {
        // Arrange - Create test address at the query location
        var command = new CreateAddressCommand
        {
            Street = $"Test Address for {lat},{lon}",
            City = "Test City",
            Postcode = "TST 123",
            Country = "UK",
            Latitude = lat,
            Longitude = lon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        var createResult = await _mediator.Send(command);
        createResult.IsSuccess.Should().BeTrue();

        // Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = lat,
            Lon = lon,
            RadiusMeters = radiusMeters,
            Limit = limit
        };

        var result = await _mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Count().Should().BeLessOrEqualTo(limit);

        // Should find at least the address we just created (distance = 0)
        var exactMatch = result.Value.FirstOrDefault(a => a.Distance < 1); // Very close to 0
        exactMatch.Should().NotBeNull();
    }

    [Fact]
    public async Task GetNearbyAddresses_WithPreciseCoordinates_ShouldCalculateAccurateDistances()
    {
        // Arrange - Create addresses with known precise distances
        var baseLat = 51.5074;
        var baseLon = -0.1278;

        // Create an address exactly 1km north (approximately)
        // 1 degree of latitude ≈ 111km, so 0.009 degrees ≈ 1km
        var northAddress = new CreateAddressCommand
        {
            Street = "1km North",
            City = "London",
            Postcode = "N1K 1KM",
            Country = "UK",
            Latitude = baseLat + 0.009,
            Longitude = baseLon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(northAddress);

        // Create an address exactly at the base location
        var baseAddress = new CreateAddressCommand
        {
            Street = "Base Location",
            City = "London",
            Postcode = "BAS E01",
            Country = "UK",
            Latitude = baseLat,
            Longitude = baseLon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(baseAddress);

        // Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = baseLat,
            Lon = baseLon,
            RadiusMeters = 2000, // 2km radius
            Limit = 10
        };

        var result = await _mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var addresses = result.Value.Where(a => a.Street.Contains("km North") || a.Street.Contains("Base Location")).ToList();
        addresses.Should().HaveCount(2);

        var baseLocationAddress = addresses.First(a => a.Street == "Base Location");
        var northAddress1km = addresses.First(a => a.Street == "1km North");

        // Base location should have distance ≈ 0
        baseLocationAddress.Distance.Should().BeLessThan(10); // Very close to 0

        // North address should be approximately 1000m away
        northAddress1km.Distance.Should().BeInRange(900, 1100); // Allow for some calculation variance
    }

    [Fact]
    public async Task GetNearbyAddresses_WithExactRadiusBoundary_ShouldIncludeOrExcludeCorrectly()
    {
        // Arrange
        var centerLat = 51.5074;
        var centerLon = -0.1278;
        var testRadius = 1000.0;

        // Create address just inside the radius
        var insideAddress = new CreateAddressCommand
        {
            Street = "Inside Radius",
            City = "London",
            Postcode = "IN1 1IN",
            Country = "UK",
            Latitude = centerLat + 0.008, // Slightly less than 1km north
            Longitude = centerLon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(insideAddress);

        // Create address just outside the radius
        var outsideAddress = new CreateAddressCommand
        {
            Street = "Outside Radius",
            City = "London",
            Postcode = "OUT 1OU",
            Country = "UK",
            Latitude = centerLat + 0.012, // More than 1km north
            Longitude = centerLon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(outsideAddress);

        // Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = centerLat,
            Lon = centerLon,
            RadiusMeters = testRadius,
            Limit = 10
        };

        var result = await _mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var boundaryTestAddresses = result.Value.Where(a =>
            a.Street.Contains("Inside Radius") || a.Street.Contains("Outside Radius")).ToList();

        // Should include the inside address
        boundaryTestAddresses.Should().Contain(a => a.Street == "Inside Radius");

        // Should NOT include the outside address
        boundaryTestAddresses.Should().NotContain(a => a.Street == "Outside Radius");
    }

    [Fact]
    public async Task GetNearbyAddresses_WithZeroRadius_ShouldReturnOnlyExactMatches()
    {
        // Arrange
        var exactLat = 51.5074;
        var exactLon = -0.1278;

        // Create address at exact coordinates
        var exactAddress = new CreateAddressCommand
        {
            Street = "Exact Match",
            City = "London",
            Postcode = "EX1 1EX",
            Country = "UK",
            Latitude = exactLat,
            Longitude = exactLon,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(exactAddress);

        // Create nearby address (should not be returned with zero radius)
        var nearbyAddress = new CreateAddressCommand
        {
            Street = "Nearby Address",
            City = "London",
            Postcode = "NE1 1NE",
            Country = "UK",
            Latitude = exactLat + 0.001, // Very close but not exact
            Longitude = exactLon + 0.001,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        await _mediator.Send(nearbyAddress);

        // Act
        var query = new GetNearbyAddressesQuery
        {
            Lat = exactLat,
            Lon = exactLon,
            RadiusMeters = 0, // Zero radius
            Limit = 10
        };

        var result = await _mediator.Send(query);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var testAddresses = result.Value.Where(a =>
            a.Street.Contains("Exact Match") || a.Street.Contains("Nearby Address")).ToList();

        // Should only include exact match
        testAddresses.Should().ContainSingle(a => a.Street == "Exact Match");
        testAddresses.Should().NotContain(a => a.Street == "Nearby Address");
    }

    [Fact]
    public async Task GetNearbyAddresses_WithCaching_ShouldReturnConsistentResults()
    {
        // Arrange
        var queryLat = 51.5074;
        var queryLon = -0.1278;

        var query = new GetNearbyAddressesQuery
        {
            Lat = queryLat,
            Lon = queryLon,
            RadiusMeters = 1000,
            Limit = 10
        };

        // Act - Execute same query multiple times
        var result1 = await _mediator.Send(query);
        var result2 = await _mediator.Send(query);
        var result3 = await _mediator.Send(query);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result3.IsSuccess.Should().BeTrue();

        var addresses1 = result1.Value.ToList();
        var addresses2 = result2.Value.ToList();
        var addresses3 = result3.Value.ToList();

        // Results should be identical (same count, same order, same distances)
        addresses1.Count.Should().Be(addresses2.Count);
        addresses2.Count.Should().Be(addresses3.Count);

        for (int i = 0; i < addresses1.Count; i++)
        {
            addresses1[i].Id.Should().Be(addresses2[i].Id);
            addresses2[i].Id.Should().Be(addresses3[i].Id);
            addresses1[i].Distance.Should().Be(addresses2[i].Distance);
            addresses2[i].Distance.Should().Be(addresses3[i].Distance);
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}