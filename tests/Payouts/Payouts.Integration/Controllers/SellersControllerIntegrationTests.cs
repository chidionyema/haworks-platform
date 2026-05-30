using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.Payouts.Application.Sellers.Commands.RegisterSeller;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Haworks.Payouts.Integration.Controllers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class SellersControllerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;

    public SellersControllerIntegrationTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<PayoutsDbContext>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _scope.Dispose();
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Register_Should_Create_Seller_Profile_Successfully()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var command = new RegisterSellerCommand(sellerId, "test@example.com", "test-key");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var json = JsonConvert.SerializeObject(command);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/Sellers", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        Assert.NotNull(result!.profileId);

        // Verify database
        var profile = await _db.SellerProfiles.FindAsync(Guid.Parse((string)result.profileId));
        profile.Should().NotBeNull();
        profile!.SellerId.Should().Be(sellerId);
        profile.ExternalProviderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_Should_Return_Existing_Profile_When_Seller_Already_Registered()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Pre-create seller profile
        var existingProfile = SellerProfile.Create(sellerId);
        existingProfile.ExternalProviderId = "acct_existing";
        _db.SellerProfiles.Add(existingProfile);
        await _db.SaveChangesAsync();

        var command = new RegisterSellerCommand(sellerId, "test@example.com", "test-key");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var json = JsonConvert.SerializeObject(command);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/Sellers", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        var returnedProfileId = Guid.Parse((string)result!.profileId);
        returnedProfileId.Should().Be(existingProfile.Id);
    }

    [Fact]
    public async Task Register_Should_Return_Forbidden_When_User_Tries_To_Register_Another_Seller()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();
        var command = new RegisterSellerCommand(otherSellerId, "other@example.com", "test-key");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var json = JsonConvert.SerializeObject(command);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/Sellers", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_Should_Return_Unauthorized_When_No_Auth_Header()
    {
        // Arrange
        var command = new RegisterSellerCommand(Guid.NewGuid(), "test@example.com", "test-key");

        var json = JsonConvert.SerializeObject(command);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/Sellers", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_Link_For_Valid_Seller()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Pre-create seller profile
        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = "acct_test123";
        _db.SellerProfiles.Add(profile);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var validReturnUrl = "https://example.com/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={validReturnUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        Assert.NotNull(result!.url);
        ((string)result.url).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_BadRequest_For_Invalid_ReturnUrl()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var invalidReturnUrl = "http://localhost/return"; // HTTP not HTTPS
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={invalidReturnUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid redirect URL");
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_BadRequest_For_Localhost_URL()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var localhostReturnUrl = "https://localhost/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={localhostReturnUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid redirect URL");
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_BadRequest_For_Private_IP_Address()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var privateIpUrl = "https://192.168.1.1/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={privateIpUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid redirect URL");
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_BadRequest_For_Link_Local_IP()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var linkLocalUrl = "https://169.254.1.1/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={linkLocalUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid redirect URL");
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_Forbidden_When_User_Tries_To_Access_Another_Sellers_Link()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var validReturnUrl = "https://example.com/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{otherSellerId}/onboarding-link?returnUrl={validReturnUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_Unauthorized_When_No_Auth_Header()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var validReturnUrl = "https://example.com/return";
        var validRefreshUrl = "https://example.com/refresh";

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/Sellers/{sellerId}/onboarding-link?returnUrl={validReturnUrl}&refreshUrl={validRefreshUrl}",
            new StringContent(""));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}