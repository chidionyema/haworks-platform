using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Haworks.Location.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Haworks.Location.Infrastructure.Persistence;
using Xunit;

namespace Haworks.Location.Integration;

[Collection("Location Integration")]
public class IdempotencyTests(LocationWebAppFactory factory) : IClassFixture<LocationWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DuplicateRequest_SameIdempotencyKey_ReturnsSameId_HandlerExecutesOnce()
    {
        var idempotencyKey = $"idem-duplicate-{Guid.NewGuid()}";
        var command = new CreateAddressCommand
        {
            Street = "1 Idempotent Lane",
            City = "TestCity",
            Postcode = "ID1 1AA",
            Country = "UK",
            Latitude = 51.5,
            Longitude = -0.1,
            IdempotencyKey = idempotencyKey
        };

        // First request — handler executes, address created
        var response1 = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        var body1 = await response1.Content.ReadAsStringAsync();
        response1.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body1}");
        var id1 = await response1.Content.ReadFromJsonAsync<Guid>();

        // Second request — same key, handler should NOT execute again
        var response2 = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        response2.EnsureSuccessStatusCode();
        var id2 = await response2.Content.ReadFromJsonAsync<Guid>();

        // Both requests return the same address ID
        id1.Should().NotBeEmpty();
        id2.Should().Be(id1, "duplicate idempotency key must return cached result");

        // Verify only one address exists in the database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        var count = await db.Addresses.CountAsync(a => a.Street == "1 Idempotent Lane");
        count.Should().Be(1, "handler should only execute once for the same idempotency key");
    }

    [Fact]
    public async Task DifferentIdempotencyKeys_CreateSeparateAddresses()
    {
        var command1 = new CreateAddressCommand
        {
            Street = "2 Unique Lane",
            City = "TestCity",
            Postcode = "UN1 1AA",
            Country = "UK",
            Latitude = 52.0,
            Longitude = -1.0,
            IdempotencyKey = $"idem-unique-a-{Guid.NewGuid()}"
        };

        var command2 = new CreateAddressCommand
        {
            Street = "3 Unique Lane",
            City = "TestCity",
            Postcode = "UN2 2BB",
            Country = "UK",
            Latitude = 52.1,
            Longitude = -1.1,
            IdempotencyKey = $"idem-unique-b-{Guid.NewGuid()}"
        };

        var response1 = await _client.PostAsJsonAsync("/api/v1/addresses", command1);
        var response2 = await _client.PostAsJsonAsync("/api/v1/addresses", command2);

        response1.EnsureSuccessStatusCode();
        response2.EnsureSuccessStatusCode();

        var id1 = await response1.Content.ReadFromJsonAsync<Guid>();
        var id2 = await response2.Content.ReadFromJsonAsync<Guid>();

        id1.Should().NotBe(id2, "different idempotency keys must create separate resources");
    }

    [Fact]
    public async Task IdempotencyJournal_RecordCreatedWithCorrectFields()
    {
        var idempotencyKey = $"idem-journal-{Guid.NewGuid()}";
        var command = new CreateAddressCommand
        {
            Street = "4 Journal Lane",
            City = "TestCity",
            Postcode = "JN1 1AA",
            Country = "UK",
            Latitude = 53.0,
            Longitude = -2.0,
            IdempotencyKey = idempotencyKey
        };

        var response = await _client.PostAsJsonAsync("/api/v1/addresses", command);
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocationDbContext>();
        var journalEntry = await db.IdempotencyJournal
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey);

        journalEntry.Should().NotBeNull("journal entry must be created for idempotent commands");
        journalEntry!.CommandType.Should().Be("CreateAddressCommand");
        journalEntry.ResponseJson.Should().NotBeNullOrEmpty("response must be cached");
        journalEntry.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow, "entry must not be expired");
    }
}
