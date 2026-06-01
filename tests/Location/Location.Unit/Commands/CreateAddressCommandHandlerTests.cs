using FluentAssertions;
using Haworks.BuildingBlocks.Common;
using Haworks.Contracts.Location;
using MassTransit;
using Haworks.Location.Application.Commands;
using Haworks.Location.Application.Interfaces;
using Haworks.Location.Domain.Entities;
using Moq;
using NetTopologySuite.Geometries;
using Xunit;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Haworks.Location.Unit.Commands;

public class CreateAddressCommandHandlerTests
{
    private readonly Mock<ILocationDbContext> _dbContextMock;
    private readonly Mock<IPublishEndpoint> _publisherMock;
    private readonly Mock<IGeocodingService> _geocodingMock;
    private readonly Mock<IGeohashService> _geohashMock;
    private readonly CreateAddressCommandHandler _handler;

    public CreateAddressCommandHandlerTests()
    {
        _dbContextMock = new Mock<ILocationDbContext>();
        _publisherMock = new Mock<IPublishEndpoint>();
        _geocodingMock = new Mock<IGeocodingService>();
        _geohashMock = new Mock<IGeohashService>();
        
        // Mock DbSet
        var addresses = new List<Address>();
        var dbSetMock = CreateMockDbSet(addresses);
        _dbContextMock.Setup(x => x.Addresses).Returns(dbSetMock.Object);

        _handler = new CreateAddressCommandHandler(
            _dbContextMock.Object, 
            _publisherMock.Object,
            _geocodingMock.Object,
            _geohashMock.Object);
    }

    private static Mock<DbSet<T>> CreateMockDbSet<T>(List<T> sourceList) where T : class
    {
        var mockSet = new Mock<DbSet<T>>();
        mockSet.As<IQueryable<T>>().Setup(m => m.Provider).Returns(sourceList.AsQueryable().Provider);
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(sourceList.AsQueryable().Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(sourceList.AsQueryable().ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(sourceList.AsQueryable().GetEnumerator());
        mockSet.Setup(d => d.Add(It.IsAny<T>())).Callback<T>(sourceList.Add);
        return mockSet;
    }

    [Fact]
    public async Task Handle_WithCoords_ShouldSaveAddressAndPublishEvent()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "123 Main St",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "United Kingdom",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = "test-key-1"
        };
        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        _dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _geohashMock.Verify(x => x.Encode(command.Latitude.Value, command.Longitude.Value, 12), Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutCoords_ShouldGeocodeAndSave()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "Buckingham Palace",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            IdempotencyKey = "test-key-2"
        };
        _geocodingMock.Setup(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((51.5014, -0.1419));
        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _geohashMock.Verify(x => x.Encode(51.5014, -0.1419, 12), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenGeocodingFails_ShouldReturnFailureResult()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "Nonexistent Street",
            City = "Unknown City",
            Postcode = "UNKNOWN",
            Country = "Unknown Country",
            IdempotencyKey = "test-key-error-1"
        };

        // Mock geocoding service to return null for both full address and postcode
        _geocodingMock.Setup(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValueTuple<double, double>?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Code.Should().Be("Address.GeocodingFailed");
        result.Error.Message.Should().Contain("Could not geocode address");

        // Verify geocoding was attempted twice (full address + postcode)
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify no database operations occurred
        _dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Verify no events were published
        _publisherMock.Verify(x => x.Publish(It.IsAny<LocationUpdated>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFullAddressGeocodingFailsButPostcodeSucceeds_ShouldUsePostcodeCoordinates()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "Invalid Street Name",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            IdempotencyKey = "test-key-postcode-fallback"
        };

        // Mock full address geocoding to fail, but postcode to succeed
        _geocodingMock.SetupSequence(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValueTuple<double, double>?)null)  // Full address fails
            .ReturnsAsync((51.5074, -0.1278));                // Postcode succeeds

        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        // Verify geocoding was attempted twice
        _geocodingMock.Verify(x => x.GeocodeAsync(It.Is<string>(s => s.Contains("Invalid Street Name")), It.IsAny<CancellationToken>()), Times.Once);
        _geocodingMock.Verify(x => x.GeocodeAsync("SW1A 1AA", It.IsAny<CancellationToken>()), Times.Once);

        // Verify geohash was generated with postcode coordinates
        _geohashMock.Verify(x => x.Encode(51.5074, -0.1278, 12), Times.Once);

        // Verify database operations occurred
        _dbContextMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOnlyLatitudeProvided_ShouldTriggerGeocoding()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "123 Main St",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            Latitude = 51.5074,  // Only latitude provided
            Longitude = null,     // Longitude missing
            IdempotencyKey = "test-key-partial-coords"
        };

        _geocodingMock.Setup(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((51.5074, -0.1278));
        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify geocoding was attempted because coordinates were incomplete
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_WhenOnlyLongitudeProvided_ShouldTriggerGeocoding()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "123 Main St",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            Latitude = null,      // Latitude missing
            Longitude = -0.1278,  // Only longitude provided
            IdempotencyKey = "test-key-partial-coords-2"
        };

        _geocodingMock.Setup(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((51.5074, -0.1278));
        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify geocoding was attempted because coordinates were incomplete
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Handle_WhenDatabaseSaveThrows_ShouldPropagateException()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "123 Main St",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = "test-key-db-error"
        };

        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns("gcpvj0d9");

        // Mock database to throw exception during save
        _dbContextMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Database connection failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Be("Database connection failed");

        // Verify event was published before the save attempt
        _publisherMock.Verify(x => x.Publish(It.IsAny<LocationUpdated>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldPublishEventWithCorrectData()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "10 Downing Street",
            City = "London",
            Postcode = "SW1A 2AA",
            Country = "United Kingdom",
            Latitude = 51.5034,
            Longitude = -0.1276,
            IdempotencyKey = "test-key-event-data"
        };

        const string expectedGeohash = "gcpvj0d8x";
        _geohashMock.Setup(x => x.Encode(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns(expectedGeohash);

        LocationUpdated? publishedEvent = null;
        _publisherMock.Setup(x => x.Publish(It.IsAny<LocationUpdated>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((evt, _) => publishedEvent = (LocationUpdated)evt);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        publishedEvent.Should().NotBeNull();
        publishedEvent!.LocationId.Should().NotBeEmpty();
        publishedEvent.Address.Street.Should().Be("10 Downing Street");
        publishedEvent.Address.City.Should().Be("London");
        publishedEvent.Address.Postcode.Should().Be("SW1A 2AA");
        publishedEvent.Address.Country.Should().Be("United Kingdom");
        publishedEvent.Latitude.Should().Be(51.5034);
        publishedEvent.Longitude.Should().Be(-0.1276);
        publishedEvent.Geohash.Should().Be(expectedGeohash);
    }

    [Fact]
    public async Task Handle_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var command = new CreateAddressCommand
        {
            Street = "123 Main St",
            City = "London",
            Postcode = "SW1A 1AA",
            Country = "UK",
            IdempotencyKey = "test-key-cancellation"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _geocodingMock.Setup(x => x.GeocodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _handler.Handle(command, cts.Token));

        // Verify geocoding service received the cancellation token
        _geocodingMock.Verify(x => x.GeocodeAsync(It.IsAny<string>(), cts.Token), Times.AtLeastOnce);
    }
}
