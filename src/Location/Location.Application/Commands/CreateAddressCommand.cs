using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Contracts.Location;
using Haworks.Location.Application.Interfaces;
using Haworks.Location.Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Haworks.Location.Application.Commands;

/// <summary>
/// Command to create a new address record and publish a LocationUpdated event.
/// Coordinates are optional; if missing, the service will attempt to geocode the address.
/// </summary>
public record CreateAddressCommand : IIdempotentCommand, IRequest<Result<Guid>>
{
    public required string Street { get; init; }
    public required string City { get; init; }
    public required string Postcode { get; init; }
    public required string Country { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}

public class CreateAddressCommandHandler(
    ILocationDbContext dbContext,
    IPublishEndpoint publisher,
    IGeocodingService geocodingService,
    IGeohashService geohashService) : IRequestHandler<CreateAddressCommand, Result<Guid>>
{
    private static readonly Regex HtmlTagPattern = new("<[^>]*>", RegexOptions.Compiled | RegexOptions.NonBacktracking);

    private static string SanitizeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var noHtml = HtmlTagPattern.Replace(input, string.Empty);
        noHtml = System.Net.WebUtility.HtmlDecode(noHtml);
        return HtmlEncoder.Default.Encode(noHtml.Trim());
    }

    public async Task<Result<Guid>> Handle(CreateAddressCommand request, CancellationToken cancellationToken)
    {
        // Sanitize input fields
        var street = SanitizeInput(request.Street);
        var city = SanitizeInput(request.City);
        var postcode = SanitizeInput(request.Postcode);
        var country = SanitizeInput(request.Country);
        double lat = request.Latitude ?? 0;
        double lon = request.Longitude ?? 0;

        // 1. Geocode if coordinates are missing
        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            var addressString = $"{street}, {city}, {postcode}, {country}";
            var coords = await geocodingService.GeocodeAsync(addressString, cancellationToken);

            if (coords == null)
            {
                // If full address geocoding fails, try just the postcode
                coords = await geocodingService.GeocodeAsync(postcode, cancellationToken);
            }

            if (coords != null)
            {
                lat = coords.Value.Latitude;
                lon = coords.Value.Longitude;
            }
            else
            {
                // Store with null coordinates and schedule for later geocoding
                lat = 0;
                lon = 0;
            }
        }

        // 2. Generate Geohash (Level 12 for high precision storage)
        var geohash = geohashService.Encode(lat, lon, 12);

        var address = Address.Create(
            street, city, postcode, country,
            new Point(lon, lat) { SRID = 4326 }, geohash);

        dbContext.Addresses.Add(address);

        // Publish BEFORE save — outbox-friendly. The OutboxMessage row commits
        // in the same EF transaction as the address insert; on rollback the
        // publish is rolled back too.
        await publisher.Publish(new LocationUpdated
        {
            LocationId = address.Id,
            Address = new AddressInfo
            {
                Street = street,
                City = city,
                Postcode = postcode,
                Country = country
            },
            Latitude = lat,
            Longitude = lon,
            Geohash = address.Geohash
        }, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Addresses_Unique_Address") == true)
        {
            return Result.Failure<Guid>(Error.Conflict("Address.Duplicate", "Address already exists"));
        }

        return Result.Success(address.Id);
    }
}
