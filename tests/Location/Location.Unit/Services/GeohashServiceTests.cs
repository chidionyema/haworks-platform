using FluentAssertions;
using Haworks.Location.Infrastructure.Services;
using Xunit;

namespace Haworks.Location.Unit.Services;

public class GeohashServiceTests
{
    private readonly GeohashService _service;

    public GeohashServiceTests()
    {
        _service = new GeohashService();
    }

    [Theory]
    [InlineData(42.6, -5.6, 5, "ezs42")]          // Known test vector
    [InlineData(57.64911, 10.40744, 5, "u4prz")]  // Aarhus, Denmark
    [InlineData(38.8951, -77.0364, 5, "dqcjq")]   // Washington DC
    [InlineData(51.5074, -0.1278, 5, "gcpvj")]    // London
    [InlineData(-33.8688, 151.2093, 5, "r3gx2")]  // Sydney
    public void Encode_WithKnownTestVectors_ShouldReturnExpectedGeohash(double lat, double lon, int precision, string expected)
    {
        // Act
        var result = _service.Encode(lat, lon, precision);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(42.6, -5.6, 1, "e")]
    [InlineData(42.6, -5.6, 3, "ezs")]
    [InlineData(42.6, -5.6, 8, "ezs42e44")]
    [InlineData(42.6, -5.6, 12, "ezs42e448h2f")]
    public void Encode_WithDifferentPrecisionLevels_ShouldReturnCorrectLength(double lat, double lon, int precision, string expected)
    {
        // Act
        var result = _service.Encode(lat, lon, precision);

        // Assert
        result.Should().Be(expected);
        result.Length.Should().Be(precision);
    }

    [Fact]
    public void Encode_WithDefaultPrecision_ShouldReturn12CharacterGeohash()
    {
        // Arrange
        const double lat = 51.5074;
        const double lon = -0.1278;

        // Act
        var result = _service.Encode(lat, lon);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(12);
        result.Should().MatchRegex("^[0-9bcdefghjkmnpqrstuvwxyz]+$"); // Base32 alphabet only
    }

    [Theory]
    [InlineData(-90.0, -180.0)]  // South-west corner
    [InlineData(-90.0, 180.0)]   // South-east corner
    [InlineData(90.0, -180.0)]   // North-west corner
    [InlineData(90.0, 180.0)]    // North-east corner
    [InlineData(0.0, 0.0)]       // Origin
    public void Encode_WithBoundaryCoordinates_ShouldNotThrow(double lat, double lon)
    {
        // Act
        var result = _service.Encode(lat, lon, 5);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(5);
        result.Should().MatchRegex("^[0-9bcdefghjkmnpqrstuvwxyz]+$");
    }

    [Theory]
    [InlineData(-91.0, 0.0)]     // Latitude too low
    [InlineData(91.0, 0.0)]      // Latitude too high
    [InlineData(0.0, -181.0)]    // Longitude too low
    [InlineData(0.0, 181.0)]     // Longitude too high
    public void Encode_WithInvalidCoordinates_ShouldStillEncode(double lat, double lon)
    {
        // Note: The current implementation doesn't validate bounds,
        // it just encodes whatever is passed in

        // Act
        var result = _service.Encode(lat, lon, 5);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public void Encode_WithZeroOrNegativePrecision_ShouldReturnEmptyString(int precision)
    {
        // Act
        var result = _service.Encode(51.5074, -0.1278, precision);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Encode_WithVeryHighPrecision_ShouldHandleCorrectly()
    {
        // Arrange
        const int highPrecision = 20;
        const double lat = 51.5074;
        const double lon = -0.1278;

        // Act
        var result = _service.Encode(lat, lon, highPrecision);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(highPrecision);
        result.Should().MatchRegex("^[0-9bcdefghjkmnpqrstuvwxyz]+$");
    }

    [Fact]
    public void Encode_WithSameCoordinates_ShouldReturnSameGeohash()
    {
        // Arrange
        const double lat = 40.7589;
        const double lon = -73.9851;
        const int precision = 8;

        // Act
        var result1 = _service.Encode(lat, lon, precision);
        var result2 = _service.Encode(lat, lon, precision);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void Encode_WithVeryCloseCoordinates_ShouldReturnSimilarGeohashes()
    {
        // Arrange
        const double lat1 = 51.50740;
        const double lat2 = 51.50741; // Very small difference
        const double lon = -0.1278;
        const int precision = 8;

        // Act
        var result1 = _service.Encode(lat1, lon, precision);
        var result2 = _service.Encode(lat2, lon, precision);

        // Assert
        // Should share a common prefix since they're very close
        result1[..6].Should().Be(result2[..6]);
    }

    [Theory]
    [InlineData(51.5074, -0.1278, 5)]   // London
    [InlineData(40.7589, -73.9851, 8)]  // New York
    [InlineData(35.6762, 139.6503, 6)]  // Tokyo
    [InlineData(-33.8688, 151.2093, 7)] // Sydney
    public void Encode_WithRealWorldCoordinates_ShouldProduceValidGeohash(double lat, double lon, int precision)
    {
        // Act
        var result = _service.Encode(lat, lon, precision);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(precision);
        result.Should().MatchRegex("^[0-9bcdefghjkmnpqrstuvwxyz]+$");

        // Verify all characters are from the Base32 geohash alphabet
        const string geohashAlphabet = "0123456789bcdefghjkmnpqrstuvwxyz";
        result.Should().OnlyContain(c => geohashAlphabet.Contains(c));
    }

    [Fact]
    public void Encode_WithEquator_ShouldHandleCorrectly()
    {
        // Arrange - Points on the equator
        const double lat = 0.0;
        const double lon1 = 0.0;    // Prime meridian
        const double lon2 = 180.0;  // International date line
        const double lon3 = -180.0; // International date line (other side)

        // Act
        var result1 = _service.Encode(lat, lon1, 6);
        var result2 = _service.Encode(lat, lon2, 6);
        var result3 = _service.Encode(lat, lon3, 6);

        // Assert
        result1.Should().NotBeNullOrEmpty();
        result2.Should().NotBeNullOrEmpty();
        result3.Should().NotBeNullOrEmpty();

        // Results should be different for different longitudes
        result1.Should().NotBe(result2);
        // 180 and -180 longitude should produce the same or very similar geohash
        result2.Should().Be(result3);
    }

    [Fact]
    public void Encode_WithDateLine_ShouldHandleCorrectly()
    {
        // Arrange - Points near the international date line
        const double lat = 60.0;
        const double lon1 = 179.9;
        const double lon2 = -179.9;

        // Act
        var result1 = _service.Encode(lat, lon1, 6);
        var result2 = _service.Encode(lat, lon2, 6);

        // Assert
        result1.Should().NotBeNullOrEmpty();
        result2.Should().NotBeNullOrEmpty();

        // These points are geographically close but cross the date line,
        // so they might have different geohashes
        result1.Should().NotBe(result2);
    }
}