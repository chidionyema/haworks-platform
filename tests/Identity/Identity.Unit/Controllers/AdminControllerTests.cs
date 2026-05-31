using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MassTransit;
using Moq;
using Xunit;
using Haworks.Identity.Api.Controllers;
using Haworks.BuildingBlocks.Vault;
using Haworks.Contracts.Identity;

namespace Haworks.Identity.Unit.Controllers;

public class AdminControllerTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<IPublishEndpoint> _publishEndpointMock;
    private readonly Mock<ILogger<AdminController>> _loggerMock;
    private readonly Mock<IVaultService> _vaultServiceMock;
    private readonly Mock<VaultProbeClient> _vaultProbeClientMock;
    private readonly Mock<HttpClient> _httpClientMock;

    public AdminControllerTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _configurationMock = new Mock<IConfiguration>();
        _publishEndpointMock = new Mock<IPublishEndpoint>();
        _loggerMock = new Mock<ILogger<AdminController>>();
        _vaultServiceMock = new Mock<IVaultService>();
        _httpClientMock = new Mock<HttpClient>();
        _vaultProbeClientMock = new Mock<VaultProbeClient>(_httpClientMock.Object, new Uri("http://vault:8200"));
    }

    private AdminController CreateController() =>
        new(_serviceProviderMock.Object, _configurationMock.Object, _publishEndpointMock.Object, _loggerMock.Object);

    [Fact]
    public async Task GetVaultStatus_WhenVaultDisabled_ReturnsDisabledStatus()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(false);
        var controller = CreateController();

        // Act
        var result = await controller.GetVaultStatus(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        // Use reflection to access anonymous object properties
        var statusProp = response.GetType().GetProperty("status");
        var enabledProp = response.GetType().GetProperty("enabled");
        Assert.Equal("Disabled", statusProp?.GetValue(response));
        Assert.Equal(false, enabledProp?.GetValue(response));
    }

    [Fact]
    public async Task GetVaultStatus_WhenVaultEnabledButProbeClientNotRegistered_ReturnsDisabledStatus()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<VaultProbeClient>()).Returns((VaultProbeClient)null!);
        var controller = CreateController();

        // Act
        var result = await controller.GetVaultStatus(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var statusProp = response.GetType().GetProperty("status");
        Assert.Equal("Disabled", statusProp?.GetValue(response));
    }

    [Fact]
    public async Task GetVaultStatus_WhenVaultHealthy_ReturnsHealthyStatus()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<VaultProbeClient>()).Returns(_vaultProbeClientMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService<IVaultService>()).Returns(_vaultServiceMock.Object);

        var leaseExpiry = DateTime.UtcNow.AddHours(1);
        _vaultServiceMock.Setup(v => v.LeaseExpiryFor("haworks-identity")).Returns(leaseExpiry);

        // Mock successful HTTP response
        var httpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        _vaultProbeClientMock.Setup(v => v.Client.GetAsync("/v1/sys/health", It.IsAny<CancellationToken>()))
            .ReturnsAsync(httpResponseMessage);

        var controller = CreateController();

        // Act
        var result = await controller.GetVaultStatus(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        var statusProp = response.GetType().GetProperty("status");
        Assert.Equal("Healthy", statusProp?.GetValue(response));
    }

    [Fact]
    public async Task GetVaultStatus_WhenVaultUnreachable_ReturnsServiceUnavailable()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<VaultProbeClient>()).Returns(_vaultProbeClientMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService<IVaultService>()).Returns(_vaultServiceMock.Object);

        // Mock HTTP exception
        _vaultProbeClientMock.Setup(v => v.Client.GetAsync("/v1/sys/health", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var controller = CreateController();

        // Act
        var result = await controller.GetVaultStatus(CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);

        var response = statusResult.Value;
        Assert.NotNull(response);

        var statusProp = response.GetType().GetProperty("status");
        Assert.Equal("Unreachable", statusProp?.GetValue(response));
    }

    [Fact]
    public void RotateCredentials_WhenVaultDisabled_ReturnsServiceUnavailable()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(false);
        var controller = CreateController();

        // Act
        var result = controller.RotateCredentials();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);

        var response = statusResult.Value;
        Assert.NotNull(response);

        var statusProp = response.GetType().GetProperty("status");
        Assert.Equal("Disabled", statusProp?.GetValue(response));
    }

    [Fact]
    public void RotateCredentials_WhenVaultServiceNotRegistered_ReturnsServiceUnavailable()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<IVaultService>()).Returns((IVaultService)null!);
        var controller = CreateController();

        // Act
        var result = controller.RotateCredentials();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public void RotateCredentials_WhenVaultEnabled_ReturnsAcceptedWithSessionId()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<IVaultService>()).Returns(_vaultServiceMock.Object);
        var controller = CreateController();

        var customSessionId = Guid.NewGuid();

        // Act
        var result = controller.RotateCredentials("test-role", customSessionId);

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        var response = acceptedResult.Value;
        Assert.NotNull(response);

        var sessionIdProp = response.GetType().GetProperty("sessionId");
        var statusProp = response.GetType().GetProperty("status");
        var roleNameProp = response.GetType().GetProperty("roleName");

        Assert.Equal(customSessionId, sessionIdProp?.GetValue(response));
        Assert.Equal("Rotating", statusProp?.GetValue(response));
        Assert.Equal("test-role", roleNameProp?.GetValue(response));
    }

    [Fact]
    public void RotateCredentials_WhenSessionIdNotProvided_GeneratesNewSessionId()
    {
        // Arrange
        _configurationMock.Setup(c => c.GetValue("Vault:Enabled", false)).Returns(true);
        _serviceProviderMock.Setup(sp => sp.GetService<IVaultService>()).Returns(_vaultServiceMock.Object);
        var controller = CreateController();

        // Act
        var result = controller.RotateCredentials();

        // Assert
        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        var response = acceptedResult.Value;
        Assert.NotNull(response);

        var sessionIdProp = response.GetType().GetProperty("sessionId");
        var sessionId = sessionIdProp?.GetValue(response);

        Assert.NotNull(sessionId);
        Assert.IsType<Guid>(sessionId);
        Assert.NotEqual(Guid.Empty, (Guid)sessionId);
    }
}