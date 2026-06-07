using FluentAssertions;
using Haworks.Realtime.Api.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Haworks.Realtime.Unit.Infrastructure;

public class RedisHealthCheckTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly RedisHealthCheck _sut;

    public RedisHealthCheckTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _sut = new RedisHealthCheck(_redisMock.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnHealthy_When_RedisIsConnected()
    {
        // Arrange
        _redisMock.Setup(x => x.IsConnected).Returns(true);
        var context = new HealthCheckContext();

        // Act
        var result = await _sut.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnUnhealthy_When_RedisIsNotConnected()
    {
        // Arrange
        _redisMock.Setup(x => x.IsConnected).Returns(false);
        var context = new HealthCheckContext();

        // Act
        var result = await _sut.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Redis not connected");
    }

    [Fact]
    public async Task CheckHealthAsync_Should_ReturnUnhealthy_When_ExceptionIsThrown()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Redis connection failed");
        _redisMock.Setup(x => x.IsConnected).Throws(expectedException);
        var context = new HealthCheckContext();

        // Act
        var result = await _sut.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Redis connection failed");
    }

    [Theory]
    [InlineData(true, HealthStatus.Healthy)]
    [InlineData(false, HealthStatus.Unhealthy)]
    public async Task CheckHealthAsync_Should_ReflectConnectionState(bool isConnected, HealthStatus expectedStatus)
    {
        // Arrange
        _redisMock.Setup(x => x.IsConnected).Returns(isConnected);
        var context = new HealthCheckContext();

        // Act
        var result = await _sut.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(expectedStatus);
    }
}