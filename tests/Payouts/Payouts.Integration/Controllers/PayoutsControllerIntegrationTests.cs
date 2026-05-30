using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Haworks.Payouts.Integration.Controllers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class PayoutsControllerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;

    public PayoutsControllerIntegrationTests(PayoutsWebAppFactory factory)
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
    public async Task GetPayouts_Should_Return_Payouts_When_User_Owns_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create seller profile
        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = "acct_test123";
        _db.SellerProfiles.Add(profile);

        // Create payouts
        var payout1 = Payout.Create(sellerId, 5000L, "USD");
        var payout2 = Payout.Create(sellerId, 3000L, "USD");
        payout2.MarkSucceeded();

        _db.Payouts.AddRange(payout1, payout2);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{sellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic[]>(content);

        result.Should().HaveCount(2);
        var payoutIds = result!.Select(p => (string)p.id).ToList();
        payoutIds.Should().Contain(payout1.Id.ToString());
        payoutIds.Should().Contain(payout2.Id.ToString());
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Empty_Array_When_No_Payouts_Exist()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{sellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic[]>(content);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Forbidden_When_User_Tries_To_Access_Another_Users_Payouts()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{otherSellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Unauthorized_When_No_Auth_Header()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{sellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Only_Sellers_Payouts()
    {
        // Arrange
        var sellerId1 = Guid.NewGuid();
        var sellerId2 = Guid.NewGuid();

        // Create payouts for two different sellers
        var payout1 = Payout.Create(sellerId1, 5000L, "USD");
        var payout2 = Payout.Create(sellerId2, 3000L, "USD");

        _db.Payouts.AddRange(payout1, payout2);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId1.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{sellerId1}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic[]>(content);

        result.Should().HaveCount(1);
        var payoutId = (string)result![0].id;
        payoutId.Should().Be(payout1.Id.ToString());
    }

    [Fact]
    public async Task GetPayouts_Should_Filter_By_SellerId_Correctly()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create multiple payouts with different statuses
        var pendingPayout = Payout.Create(sellerId, 1000L, "USD");
        var completedPayout = Payout.Create(sellerId, 2000L, "USD");
        completedPayout.MarkSucceeded();
        var failedPayout = Payout.Create(sellerId, 3000L, "USD");
        failedPayout.MarkFailed("Test failure");

        _db.Payouts.AddRange(pendingPayout, completedPayout, failedPayout);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Payouts/seller/{sellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic[]>(content);

        result.Should().HaveCount(3);
        var payoutIds = result!.Select(p => (string)p.id).ToList();
        payoutIds.Should().Contain(pendingPayout.Id.ToString());
        payoutIds.Should().Contain(completedPayout.Id.ToString());
        payoutIds.Should().Contain(failedPayout.Id.ToString());
    }
}