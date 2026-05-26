using Haworks.BuildingBlocks.Persistence;
using NetTopologySuite.Geometries;
using System.Text.Json;

namespace Haworks.Location.Domain.Entities;

/// <summary>
/// Represents a master address record with geospatial coordinates.
/// </summary>
public class Address : AuditableEntity
{
    public Address() : base() { }

    public Address(Guid id) : base(id) { }

    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Postcode { get; set; } = null!;
    public string Country { get; set; } = null!;
    
    /// <summary>
    /// Geodetic coordinates (SRID 4326).
    /// </summary>
    public Point Coordinates { get; set; } = null!;
    
    /// <summary>
    /// 12-character precision geohash for grid-based pre-filtering.
    /// </summary>
    public string Geohash { get; set; } = null!;
    
    /// <summary>
    /// Flexible JSON metadata for region, district, or business-specific tags.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Validates that the stored metadata is valid JSON.
    /// </summary>
    public bool IsMetadataValid()
    {
        if (string.IsNullOrEmpty(Metadata))
            return true;

        try
        {
            JsonDocument.Parse(Metadata);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that the geohash matches the coordinates (within reasonable precision).
    /// </summary>
    public bool IsGeohashValid(string expectedGeohash)
    {
        if (string.IsNullOrEmpty(Geohash) || string.IsNullOrEmpty(expectedGeohash))
            return false;

        // Compare first 8 characters (~19m precision) to allow for minor differences
        var minLength = Math.Min(8, Math.Min(Geohash.Length, expectedGeohash.Length));
        return string.Equals(
            Geohash[..minLength],
            expectedGeohash[..minLength],
            StringComparison.OrdinalIgnoreCase);
    }
}
