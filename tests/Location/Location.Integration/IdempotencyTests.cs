using System.Net.Http.Json;
using FluentAssertions;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Location.Application.Commands;
using Haworks.Location.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Location.Integration;

[Collection("Location Integration")]
public class IdempotencyTests(LocationWebAppFactory factory) : IClassFixture<LocationWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DuplicateRequest_SameIdempotencyKey_ReturnsSameId_HandlerExecutesOnce()
    {
        // Arrange
        var idempotencyKey = $"idem-dup-{Guid.NewGuid()}";
        var command = new CreateAddressCommand
        {
            Street = "1 Duplicate St",
            City = "TestCity",
            Postcode = "DUP 001",
            Country = "TestLand",
            Latitude = 52.0,
            Longitude = -1.0,
            IdempotencyKey = idempotencyKey
        };

        // Act - send same command twice with same key
        var response1 = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        response1.EnsureSuccessStatusCode();
        var id1 = await response1.Content.ReadFromJsonAsync<Guid>();

        var response2 = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        response2.EnsureSuccessStatusCode();
        var id2 = await response2.Content.ReadFromJsonAsync<Guid>();

        // Assert - both return the same Guid
        id1.Should().NotBeEmpty();
        id2.Should().Be(id1, "the second request should return the cached result");

        // Assert - only ONE address exists in the database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        var addressCount = await db.Addresses
            .CountAsync(a => a.Postcode == "DUP 001");
        addressCount.Should().Be(1, "the handler should have executed only once");
    }

    [Fact]
    public async Task DifferentIdempotencyKeys_CreateSeparateAddresses()
    {
        // Arrange
        var command1 = new CreateAddressCommand
        {
            Street = "2 Alpha Rd",
            City = "AlphaCity",
            Postcode = "ALP 001",
            Country = "TestLand",
            Latitude = 53.0,
            Longitude = -2.0,
            IdempotencyKey = $"idem-diff-a-{Guid.NewGuid()}"
        };

        var command2 = new CreateAddressCommand
        {
            Street = "3 Beta Rd",
            City = "BetaCity",
            Postcode = "BET 001",
            Country = "TestLand",
            Latitude = 54.0,
            Longitude = -3.0,
            IdempotencyKey = $"idem-diff-b-{Guid.NewGuid()}"
        };

        // Act
        var response1 = await _client.PostAsJsonAsync("/api/v1/addresses", command1);
        response1.EnsureSuccessStatusCode();
        var id1 = await response1.Content.ReadFromJsonAsync<Guid>();

        var response2 = await _client.PostAsJsonAsync("/api/v1/addresses", command2);
        response2.EnsureSuccessStatusCode();
        var id2 = await response2.Content.ReadFromJsonAsync<Guid>();

        // Assert - different keys produce different resources
        id1.Should().NotBeEmpty();
        id2.Should().NotBeEmpty();
        id2.Should().NotBe(id1, "different idempotency keys should create different addresses");
    }

    [Fact]
    public async Task IdempotencyJournal_RecordCreatedWithCorrectFields()
    {
        // Arrange
        var idempotencyKey = $"idem-journal-{Guid.NewGuid()}";
        var command = new CreateAddressCommand
        {
            Street = "4 Journal Ln",
            City = "JournalCity",
            Postcode = "JRN 001",
            Country = "TestLand",
            Latitude = 55.0,
            Longitude = -4.0,
            IdempotencyKey = idempotencyKey
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        response.EnsureSuccessStatusCode();

        // Assert - journal entry exists with correct fields
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        var entry = await db.IdempotencyJournal
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);

        entry.Should().NotBeNull("a journal entry should be created for the idempotency key");
        entry!.CommandType.Should().Be(nameof(CreateAddressCommand));
        entry.ResponseJson.Should().NotBeNullOrWhiteSpace("the cached response should be stored");
        entry.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow, "the entry should expire in the future");
    }
}
