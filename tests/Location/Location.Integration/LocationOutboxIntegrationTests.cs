using FluentAssertions;
using Haworks.Contracts.Location;
using Haworks.Location.Application.Commands;
using Haworks.Location.Infrastructure.Persistence;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Location.Integration;

[Collection("Integration Tests")]
public class LocationOutboxIntegrationTests : IClassFixture<LocationWebAppFactory>
{
    private readonly LocationWebAppFactory _factory;
    private readonly IServiceScope _scope;
    private readonly LocationDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly ITestHarness _testHarness;
    private readonly TestGeocodingService _geocodingService;

    public LocationOutboxIntegrationTests(LocationWebAppFactory factory)
    {
        _factory = factory;
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        _mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
        _testHarness = _scope.ServiceProvider.GetRequiredService<ITestHarness>();
        _geocodingService = _scope.ServiceProvider.GetRequiredService<TestGeocodingService>();
    }

    [Fact]
    public async Task CreateAddressCommand_ShouldPublishLocationUpdatedEventViaOutbox()
    {
        // Arrange
        await _testHarness.Start();

        var command = new CreateAddressCommand
        {
            Street = "123 Integration Test St",
            City = "London",
            Postcode = "IT1 1IT",
            Country = "United Kingdom",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        try
        {
            // Act
            var result = await _mediator.Send(command);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeEmpty();

            // Verify the address was created in the database
            var address = await _dbContext.Addresses
                .FirstOrDefaultAsync(a => a.Id == result.Value);

            address.Should().NotBeNull();
            address!.Street.Should().Be("123 Integration Test St");
            address.City.Should().Be("London");
            address.Postcode.Should().Be("IT1 1IT");
            address.Country.Should().Be("United Kingdom");

            // Verify the event was published via outbox
            var published = await _testHarness.Published.Any<LocationUpdated>();
            published.Should().BeTrue();

            // Verify the published event contains correct data
            var publishedMessage = _testHarness.Published.Select<LocationUpdated>().FirstOrDefault();
            publishedMessage.Should().NotBeNull();

            var eventData = publishedMessage!.Context.Message;
            eventData.LocationId.Should().Be(result.Value);
            eventData.Address.Street.Should().Be("123 Integration Test St");
            eventData.Address.City.Should().Be("London");
            eventData.Address.Postcode.Should().Be("IT1 1IT");
            eventData.Address.Country.Should().Be("United Kingdom");
            eventData.Latitude.Should().Be(51.5074);
            eventData.Longitude.Should().Be(-0.1278);
            eventData.Geohash.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await _testHarness.Stop();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_WithGeocoding_ShouldPublishEventWithGeocodedCoordinates()
    {
        // Arrange
        await _testHarness.Start();

        const string testAddress = "10 Downing Street, London, SW1A 2AA, UK";
        const double expectedLat = 51.5034;
        const double expectedLon = -0.1276;

        _geocodingService.SetResponse(testAddress, (expectedLat, expectedLon));

        var command = new CreateAddressCommand
        {
            Street = "10 Downing Street",
            City = "London",
            Postcode = "SW1A 2AA",
            Country = "UK",
            // No coordinates provided - should trigger geocoding
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        try
        {
            // Act
            var result = await _mediator.Send(command);

            // Assert
            result.IsSuccess.Should().BeTrue();

            // Verify the event was published with geocoded coordinates
            var published = await _testHarness.Published.Any<LocationUpdated>();
            published.Should().BeTrue();

            var publishedMessage = _testHarness.Published.Select<LocationUpdated>().FirstOrDefault();
            publishedMessage.Should().NotBeNull();

            var eventData = publishedMessage!.Context.Message;
            eventData.LocationId.Should().Be(result.Value);
            eventData.Latitude.Should().Be(expectedLat);
            eventData.Longitude.Should().Be(expectedLon);
        }
        finally
        {
            await _testHarness.Stop();
            _geocodingService.ClearResponses();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_WhenGeocodingFails_ShouldNotPublishEvent()
    {
        // Arrange
        await _testHarness.Start();

        const string testAddress = "Nonexistent Address, Unknown City, UNK000, Unknown";
        _geocodingService.SetResponse(testAddress, null);
        _geocodingService.SetResponse("UNK000", null); // Postcode fallback also fails

        var command = new CreateAddressCommand
        {
            Street = "Nonexistent Address",
            City = "Unknown City",
            Postcode = "UNK000",
            Country = "Unknown",
            // No coordinates provided - geocoding will fail
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        try
        {
            // Act
            var result = await _mediator.Send(command);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Code.Should().Be("Address.GeocodingFailed");

            // Verify no event was published
            var published = await _testHarness.Published.Any<LocationUpdated>();
            published.Should().BeFalse();

            // Verify no address was created in the database
            var addressExists = await _dbContext.Addresses.AnyAsync(a => a.Street == "Nonexistent Address");
            addressExists.Should().BeFalse();
        }
        finally
        {
            await _testHarness.Stop();
            _geocodingService.ClearResponses();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_WithDuplicateIdempotencyKey_ShouldNotPublishDuplicateEvents()
    {
        // Arrange
        await _testHarness.Start();

        var idempotencyKey = Guid.NewGuid().ToString();
        var command = new CreateAddressCommand
        {
            Street = "Idempotency Test St",
            City = "London",
            Postcode = "ID1 1ID",
            Country = "UK",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = idempotencyKey
        };

        try
        {
            // Act - Send command twice with same idempotency key
            var result1 = await _mediator.Send(command);
            var result2 = await _mediator.Send(command);

            // Assert
            result1.IsSuccess.Should().BeTrue();
            result2.IsSuccess.Should().BeTrue();

            // Both results should return the same address ID
            result1.Value.Should().Be(result2.Value);

            // Only one address should exist in database
            var addresses = await _dbContext.Addresses
                .Where(a => a.Street == "Idempotency Test St")
                .ToListAsync();

            addresses.Should().HaveCount(1);

            // Only one event should have been published (idempotency protection)
            var publishedEvents = _testHarness.Published.Select<LocationUpdated>().ToList();
            publishedEvents.Should().HaveCount(1);
        }
        finally
        {
            await _testHarness.Stop();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_TransactionRollback_ShouldNotPublishEvent()
    {
        // Arrange
        await _testHarness.Start();

        var command = new CreateAddressCommand
        {
            Street = "Transaction Test St",
            City = "London",
            Postcode = "TR1 1TR",
            Country = "UK",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        try
        {
            // Simulate a transaction that should rollback
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var result = await _mediator.Send(command);
                result.IsSuccess.Should().BeTrue();

                // Force rollback
                await transaction.RollbackAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // Verify no address exists in database after rollback
            var address = await _dbContext.Addresses
                .FirstOrDefaultAsync(a => a.Street == "Transaction Test St");
            address.Should().BeNull();

            // Verify no event was published due to outbox rollback
            // Note: This test may need adjustment based on how the outbox handles transactions
            var published = await _testHarness.Published.Any<LocationUpdated>(x =>
                x.Context.Message.Address.Street == "Transaction Test St");
            published.Should().BeFalse();
        }
        finally
        {
            await _testHarness.Stop();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_ShouldIncludeCorrectMessageHeaders()
    {
        // Arrange
        await _testHarness.Start();

        var command = new CreateAddressCommand
        {
            Street = "Headers Test St",
            City = "London",
            Postcode = "HD1 1HD",
            Country = "UK",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IdempotencyKey = Guid.NewGuid().ToString()
        };

        try
        {
            // Act
            var result = await _mediator.Send(command);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var published = await _testHarness.Published.Any<LocationUpdated>();
            published.Should().BeTrue();

            var publishedMessage = _testHarness.Published.Select<LocationUpdated>().FirstOrDefault();
            publishedMessage.Should().NotBeNull();

            // Verify message metadata
            publishedMessage!.Context.MessageId.Should().NotBeNull();
            publishedMessage.Context.CorrelationId.Should().NotBeNull();
            publishedMessage.Context.SourceAddress.Should().NotBeNull();
            publishedMessage.Context.SentTime.Should().NotBeNull();
        }
        finally
        {
            await _testHarness.Stop();
        }
    }

    [Fact]
    public async Task CreateAddressCommand_MultipleAddresses_ShouldPublishMultipleEvents()
    {
        // Arrange
        await _testHarness.Start();

        var commands = new[]
        {
            new CreateAddressCommand
            {
                Street = "Multi Test St 1",
                City = "London",
                Postcode = "MT1 1MT",
                Country = "UK",
                Latitude = 51.5074,
                Longitude = -0.1278,
                IdempotencyKey = Guid.NewGuid().ToString()
            },
            new CreateAddressCommand
            {
                Street = "Multi Test St 2",
                City = "Manchester",
                Postcode = "MT2 2MT",
                Country = "UK",
                Latitude = 53.4808,
                Longitude = -2.2426,
                IdempotencyKey = Guid.NewGuid().ToString()
            },
            new CreateAddressCommand
            {
                Street = "Multi Test St 3",
                City = "Birmingham",
                Postcode = "MT3 3MT",
                Country = "UK",
                Latitude = 52.4862,
                Longitude = -1.8904,
                IdempotencyKey = Guid.NewGuid().ToString()
            }
        };

        try
        {
            // Act
            var results = new List<Guid>();
            foreach (var command in commands)
            {
                var result = await _mediator.Send(command);
                result.IsSuccess.Should().BeTrue();
                results.Add(result.Value);
            }

            // Assert
            var publishedEvents = _testHarness.Published.Select<LocationUpdated>().ToList();
            publishedEvents.Should().HaveCount(3);

            // Verify each address has its corresponding event
            foreach (var (command, addressId) in commands.Zip(results))
            {
                var correspondingEvent = publishedEvents.FirstOrDefault(e =>
                    e.Context.Message.LocationId == addressId);

                correspondingEvent.Should().NotBeNull();
                correspondingEvent!.Context.Message.Address.Street.Should().Be(command.Street);
                correspondingEvent.Context.Message.Address.City.Should().Be(command.City);
            }
        }
        finally
        {
            await _testHarness.Stop();
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}