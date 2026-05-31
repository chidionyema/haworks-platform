using Haworks.BuildingBlocks.Persistence;
using NetTopologySuite.Geometries;

namespace Haworks.Location.Domain.Entities;

/// <summary>
/// Represents a master address record with geospatial coordinates.
/// </summary>
public class Address : AuditableEntity
{
    public Address() : base() { }

    public Address(Guid id) : base(id) { }

    public string Street { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string Postcode { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    /// <summary>
    /// Geodetic coordinates (SRID 4326).
    /// </summary>
    public Point Coordinates { get; private set; } = null!;

    /// <summary>
    /// 12-character precision geohash for grid-based pre-filtering.
    /// </summary>
    public string Geohash { get; private set; } = null!;

    /// <summary>
    /// Flexible JSON metadata for region, district, or business-specific tags.
    /// </summary>
    public string? Metadata { get; private set; }

    public static Address Create(
        string street, string city, string postcode, string country,
        Point coordinates, string geohash, string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street cannot be empty", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City cannot be empty", nameof(city));
        if (string.IsNullOrWhiteSpace(postcode))
            throw new ArgumentException("Postcode cannot be empty", nameof(postcode));
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country cannot be empty", nameof(country));
        if (coordinates?.SRID != 4326)
            throw new ArgumentException("Coordinates must use SRID 4326", nameof(coordinates));
        if (string.IsNullOrWhiteSpace(geohash))
            throw new ArgumentException("Geohash cannot be empty", nameof(geohash));

        return new Address
        {
            Street = street,
            City = city,
            Postcode = postcode,
            Country = country,
            Coordinates = coordinates,
            Geohash = geohash,
            Metadata = metadata
        };
    }
}
