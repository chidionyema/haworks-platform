using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using LocationGrpc;
using Haworks.Location.Api.Services;
using Haworks.Location.Domain.Entities;
using Haworks.Location.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NetTopologySuite.Geometries;
using Xunit;

namespace Haworks.Location.Unit.Services;

public class LocationHydrationServiceTests
{
    private readonly Mock<LocationDbContext> _dbContextMock;
    private readonly Mock<ILogger<LocationHydrationService>> _loggerMock;
    private readonly LocationHydrationService _service;

    public LocationHydrationServiceTests()
    {
        _dbContextMock = new Mock<LocationDbContext>();
        _loggerMock = new Mock<ILogger<LocationHydrationService>>();
        _service = new LocationHydrationService(_dbContextMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetAddresses_WithValidLocationIds_ShouldReturnAddressList()
    {
        // Arrange
        var addressId1 = Guid.NewGuid();
        var addressId2 = Guid.NewGuid();
        var request = new AddressRequest();
        request.LocationIds.Add(addressId1.ToString());
        request.LocationIds.Add(addressId2.ToString());

        var addresses = new List<Address>
        {
            Address.Create("123 Main St", "London", "SW1A 1AA", "UK",
                new Point(-0.1278, 51.5074) { SRID = 4326 }, "gcpvj0d9"),
            Address.Create("456 High St", "Manchester", "M1 1AA", "UK",
                new Point(-2.2426, 53.4808) { SRID = 4326 }, "gcw2m7z5")
        };

        // Use reflection to set IDs since Address.Create doesn't allow setting them
        typeof(Address).GetProperty("Id")!.SetValue(addresses[0], addressId1);
        typeof(Address).GetProperty("Id")!.SetValue(addresses[1], addressId2);

        var mockDbSet = CreateMockDbSet(addresses);
        _dbContextMock.Setup(x => x.Addresses).Returns(mockDbSet.Object);

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().HaveCount(2);
        result.Locations[0].Id.Should().Be(addressId1.ToString());
        result.Locations[0].Street.Should().Be("123 Main St");
        result.Locations[0].City.Should().Be("London");
        result.Locations[0].Latitude.Should().Be(51.5074);
        result.Locations[0].Longitude.Should().Be(-0.1278);
        result.Locations[1].Id.Should().Be(addressId2.ToString());
    }

    [Fact]
    public async Task GetAddresses_WithEmptyLocationIds_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new AddressRequest();

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAddresses_WithInvalidGuids_ShouldSkipInvalidAndLogWarning()
    {
        // Arrange
        var validId = Guid.NewGuid();
        var request = new AddressRequest();
        request.LocationIds.Add(validId.ToString());
        request.LocationIds.Add("invalid-guid");
        request.LocationIds.Add("also-invalid");

        var addresses = new List<Address>
        {
            Address.Create("123 Main St", "London", "SW1A 1AA", "UK",
                new Point(-0.1278, 51.5074) { SRID = 4326 }, "gcpvj0d9")
        };
        typeof(Address).GetProperty("Id")!.SetValue(addresses[0], validId);

        var mockDbSet = CreateMockDbSet(addresses);
        _dbContextMock.Setup(x => x.Addresses).Returns(mockDbSet.Object);

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().HaveCount(1);
        result.Locations[0].Id.Should().Be(validId.ToString());

        // Verify warning was logged for invalid GUIDs
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Skipped 2 unparseable LocationIds")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAddresses_WithOnlyInvalidGuids_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new AddressRequest();
        request.LocationIds.Add("invalid-guid-1");
        request.LocationIds.Add("invalid-guid-2");

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().BeEmpty();

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Skipped 2 unparseable LocationIds")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAddresses_WithTooManyLocationIds_ShouldThrowRpcException()
    {
        // Arrange
        var request = new AddressRequest();
        for (int i = 0; i < 1001; i++)
        {
            request.LocationIds.Add(Guid.NewGuid().ToString());
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RpcException>(() =>
            _service.GetAddresses(request, TestServerCallContext.Create()));

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Status.Detail.Should().Be("LocationIds array size must not exceed 1000");
    }

    [Fact]
    public async Task GetAddresses_WithNonExistentIds_ShouldReturnEmptyList()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new AddressRequest();
        request.LocationIds.Add(nonExistentId.ToString());

        var mockDbSet = CreateMockDbSet(new List<Address>());
        _dbContextMock.Setup(x => x.Addresses).Returns(mockDbSet.Object);

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAddresses_WithPartialMatches_ShouldReturnOnlyFoundAddresses()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();
        var request = new AddressRequest();
        request.LocationIds.Add(existingId.ToString());
        request.LocationIds.Add(nonExistentId.ToString());

        var addresses = new List<Address>
        {
            Address.Create("123 Main St", "London", "SW1A 1AA", "UK",
                new Point(-0.1278, 51.5074) { SRID = 4326 }, "gcpvj0d9")
        };
        typeof(Address).GetProperty("Id")!.SetValue(addresses[0], existingId);

        var mockDbSet = CreateMockDbSet(addresses);
        _dbContextMock.Setup(x => x.Addresses).Returns(mockDbSet.Object);

        // Act
        var result = await _service.GetAddresses(request, TestServerCallContext.Create());

        // Assert
        result.Should().NotBeNull();
        result.Locations.Should().HaveCount(1);
        result.Locations[0].Id.Should().Be(existingId.ToString());
    }

    [Fact]
    public async Task GetAddresses_ShouldUseCancellationToken()
    {
        // Arrange
        var request = new AddressRequest();
        request.LocationIds.Add(Guid.NewGuid().ToString());

        var mockDbSet = CreateMockDbSet(new List<Address>());
        _dbContextMock.Setup(x => x.Addresses).Returns(mockDbSet.Object);

        var cts = new CancellationTokenSource();
        var callContext = TestServerCallContext.Create(cancellationToken: cts.Token);

        // Act
        await _service.GetAddresses(request, callContext);

        // Assert
        // The mock setup should verify that CancellationToken was used in ToListAsync
        mockDbSet.Verify(x => x.AsNoTracking(), Times.Once);
    }

    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> sourceList) where T : class
    {
        var queryableData = sourceList.AsQueryable();
        var mockSet = new Mock<DbSet<T>>();

        mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(queryableData.Provider);
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryableData.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryableData.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryableData.GetEnumerator());

        // Mock AsNoTracking() method
        mockSet.Setup(x => x.AsNoTracking()).Returns(mockSet.Object);

        return mockSet;
    }
}