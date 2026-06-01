using System.Net;
using System.Text.Json;
using FluentAssertions;
using Haworks.Location.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Haworks.Location.Unit.Services;

public class NominatimGeocodingServiceTests
{
    private readonly Mock<ILogger<NominatimGeocodingService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly NominatimGeocodingService _service;

    public NominatimGeocodingServiceTests()
    {
        _loggerMock = new Mock<ILogger<NominatimGeocodingService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://nominatim.openstreetmap.org/")
        };

        _service = new NominatimGeocodingService(_httpClient, _loggerMock.Object);
    }

    [Fact]
    public async Task GeocodeAsync_WithValidAddress_ShouldReturnCoordinates()
    {
        // Arrange
        const string address = "10 Downing Street, London";
        const string expectedUrl = "search?q=10%20Downing%20Street%2C%20London&format=json&limit=1";

        var nominatimResponse = new[]
        {
            new
            {
                lat = "51.5034070",
                lon = "-0.1275920"
            }
        };

        var jsonResponse = JsonSerializer.Serialize(nominatimResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().EndsWith(expectedUrl)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(51.5034070, 0.0001);
        result!.Value.Longitude.Should().BeApproximately(-0.1275920, 0.0001);
    }

    [Fact]
    public async Task GeocodeAsync_WithNoResults_ShouldReturnNull()
    {
        // Arrange
        const string address = "NonexistentPlace123456";

        var jsonResponse = "[]"; // Empty array
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_WithInvalidCoordinatesInResponse_ShouldReturnNull()
    {
        // Arrange
        const string address = "Invalid Coordinates Place";

        var nominatimResponse = new[]
        {
            new
            {
                lat = "invalid",
                lon = "also_invalid"
            }
        };

        var jsonResponse = JsonSerializer.Serialize(nominatimResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_WithHttpRequestException_ShouldReturnNullAndLog()
    {
        // Arrange
        const string address = "Test Address";
        var expectedException = new HttpRequestException("Network error");

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(expectedException);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().BeNull();

        // Verify warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"HTTP error geocoding address: {address}")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GeocodeAsync_WithTimeout_ShouldReturnNullAndLog()
    {
        // Arrange
        const string address = "Test Address";
        var timeoutException = new TaskCanceledException("Request timed out");

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(timeoutException);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().BeNull();

        // Verify timeout warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"Geocoding request timed out for address: {address}")),
                timeoutException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GeocodeAsync_WithInvalidJson_ShouldReturnNullAndLog()
    {
        // Arrange
        const string address = "Test Address";
        const string invalidJson = "{ invalid json response";

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(invalidJson)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().BeNull();

        // Verify JSON error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains($"Invalid JSON response from Nominatim for address: {address}")),
                It.IsAny<JsonException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GeocodeAsync_WithSpecialCharacters_ShouldEscapeUrlProperly()
    {
        // Arrange
        const string address = "Straße für die Königin & Prinz";
        const string expectedEscapedPath = "search?q=Stra%C3%9Fe%20f%C3%BCr%20die%20K%C3%B6nigin%20%26%20Prinz&format=json&limit=1";

        var nominatimResponse = new[]
        {
            new
            {
                lat = "52.5200",
                lon = "13.4050"
            }
        };

        var jsonResponse = JsonSerializer.Serialize(nominatimResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains(expectedEscapedPath)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(52.5200, 0.0001);
        result!.Value.Longitude.Should().BeApproximately(13.4050, 0.0001);
    }

    [Fact]
    public async Task GeocodeAsync_WithCancellationToken_ShouldPassThroughCancellation()
    {
        // Arrange
        const string address = "Test Address";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.Is<CancellationToken>(ct => ct.IsCancellationRequested))
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var result = await _service.GeocodeAsync(address, cts.Token);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_WithServerError_ShouldReturnNullAndLog()
    {
        // Arrange
        const string address = "Test Address";
        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act & Assert
        // This should trigger an HttpRequestException for non-success status codes
        var result = await _service.GeocodeAsync(address);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("", null)] // Empty address
    [InlineData("   ", null)] // Whitespace only
    public async Task GeocodeAsync_WithEmptyOrWhitespaceAddress_ShouldStillMakeRequest(string address, (double, double)? expected)
    {
        // Arrange
        var jsonResponse = "[]"; // Empty array response
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _service.GeocodeAsync(address);

        // Assert
        result.Should().Be(expected);
    }
}