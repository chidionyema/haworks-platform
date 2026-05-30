using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
public class LedgerControllerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;

    public LedgerControllerIntegrationTests(PayoutsWebAppFactory factory)
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
    public async Task GetBalance_Should_Return_Balance_When_User_Owns_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var account = LedgerAccount.Create(sellerId, AccountType.SellerPending, "USD");
        account.UpdateBalance(5000L, EntryType.Credit);

        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{sellerId}?type=SellerPending&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(content);
        Assert.Equal(5000L, (long)result!.balanceCents);
        Assert.Equal("USD", (string)result.currency);
    }

    [Fact]
    public async Task GetBalance_Should_Return_Zero_When_Account_Does_Not_Exist()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{sellerId}?type=SellerPending&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(content);
        Assert.Equal(0L, (long)result!.balanceCents);
        Assert.Equal("USD", (string)result.currency);
    }

    [Fact]
    public async Task GetBalance_Should_Return_Forbidden_When_User_Tries_To_Access_Another_Users_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{otherSellerId}?type=SellerPending&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBalance_Should_Return_Unauthorized_When_No_Auth_Header()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{sellerId}?type=SellerPending&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBalance_Should_Return_Forbidden_When_Non_Admin_Queries_Platform_Account()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{sellerId}?type=PlatformHolding&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBalance_Should_Allow_Admin_To_Query_Platform_Account()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var account = LedgerAccount.Create(platformId, AccountType.PlatformHolding, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);

        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", adminId.ToString());

        // Act
        var response = await _client.GetAsync($"/api/v1/Ledger/balance/{platformId}?type=PlatformHolding&currency=USD");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(content);
        Assert.Equal(10000L, (long)result!.balanceCents);
    }
}