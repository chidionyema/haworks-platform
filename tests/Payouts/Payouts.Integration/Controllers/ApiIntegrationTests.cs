using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.Payouts.Application.Sellers.Commands.RegisterSeller;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Haworks.Payouts.Integration.Controllers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class ApiIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly PayoutsDbContext _db;

    public ApiIntegrationTests(PayoutsWebAppFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<PayoutsDbContext>();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetBalance_Should_Return_Balance_For_Valid_User()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currency = "USD";
        var balanceCents = 15000L;

        // Create account with balance
        var account = LedgerAccount.Create(userId, AccountType.SellerPending, currency);
        account.UpdateBalance(balanceCents, EntryType.Credit);
        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _client.SetTestAuth(userId.ToString(), ["User"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/ledger/balance/{userId}?type={AccountType.SellerPending}&currency={currency}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        result.GetProperty("balanceCents").GetInt64().Should().Be(balanceCents);
        result.GetProperty("currency").GetString().Should().Be(currency);
    }

    [Fact]
    public async Task GetBalance_Should_Return_Forbidden_For_Wrong_User()
    {
        // Arrange
        var ownerUserId = Guid.NewGuid();
        var requestingUserId = Guid.NewGuid();

        var account = LedgerAccount.Create(ownerUserId, AccountType.SellerPending, "USD");
        account.UpdateBalance(10000L, EntryType.Credit);
        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _client.SetTestAuth(requestingUserId.ToString(), ["User"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/ledger/balance/{ownerUserId}?type={AccountType.SellerPending}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBalance_Should_Allow_Admin_To_View_Platform_Accounts()
    {
        // Arrange
        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var adminUserId = Guid.NewGuid();

        var account = LedgerAccount.Create(platformId, AccountType.PlatformHolding, "USD");
        account.UpdateBalance(50000L, EntryType.Credit);
        _db.LedgerAccounts.Add(account);
        await _db.SaveChangesAsync();

        _client.SetTestAuth(adminUserId.ToString(), ["Admin"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/ledger/balance/{platformId}?type={AccountType.PlatformHolding}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBalance_Should_Forbid_NonAdmin_From_Platform_Accounts()
    {
        // Arrange
        var platformId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var userId = Guid.NewGuid();

        _client.SetTestAuth(userId.ToString(), ["User"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/ledger/balance/{platformId}?type={AccountType.PlatformHolding}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Seller_Payouts()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        // Create test payouts
        var payout1 = Payout.Create(sellerId, 10000L, "USD", "ext-1");
        var payout2 = Payout.Create(sellerId, 15000L, "USD", "ext-2");
        _db.Payouts.AddRange(payout1, payout2);
        await _db.SaveChangesAsync();

        _client.SetTestAuth(sellerId.ToString(), ["User"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/payouts/seller/{sellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        result.GetProperty("items").EnumerateArray().Count().Should().Be(2);
    }

    [Fact]
    public async Task GetPayouts_Should_Return_Forbidden_For_Wrong_Seller()
    {
        // Arrange
        var ownerSellerId = Guid.NewGuid();
        var requestingSellerId = Guid.NewGuid();

        _client.SetTestAuth(requestingSellerId.ToString(), ["User"]);

        // Act
        var response = await _client.GetAsync($"/api/v1/payouts/seller/{ownerSellerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RegisterSeller_Should_Create_Seller_Profile()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var command = new RegisterSellerCommand(sellerId, "test@example.com", "idem-123");

        _client.SetTestAuth(sellerId.ToString(), ["User"]);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/sellers", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        var profileId = result.GetProperty("profileId").GetGuid();
        profileId.Should().NotBeEmpty();

        // Verify in database
        var profile = await _db.SellerProfiles.FindAsync(profileId);
        profile.Should().NotBeNull();
        profile!.SellerId.Should().Be(sellerId);
    }

    [Fact]
    public async Task RegisterSeller_Should_Return_Forbidden_For_Wrong_User()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var wrongUserId = Guid.NewGuid();
        var command = new RegisterSellerCommand(sellerId, "test@example.com");

        _client.SetTestAuth(wrongUserId.ToString(), ["User"]);

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/sellers", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_Link_For_Valid_URLs()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/onboarding/complete";
        var refreshUrl = "https://example.com/onboarding/refresh";

        _client.SetTestAuth(sellerId.ToString(), ["User"]);

        // Act
        var response = await _client.PostAsync($"/api/v1/sellers/{sellerId}/onboarding-link?returnUrl={Uri.EscapeDataString(returnUrl)}&refreshUrl={Uri.EscapeDataString(refreshUrl)}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);
        result.GetProperty("url").GetString().Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("http://example.com/insecure")] // HTTP not allowed
    [InlineData("https://localhost/danger")] // localhost not allowed
    [InlineData("https://127.0.0.1/danger")] // loopback not allowed
    [InlineData("https://10.0.0.1/private")] // private IP not allowed
    [InlineData("https://192.168.1.1/internal")] // private IP not allowed
    [InlineData("javascript:alert('xss')")] // invalid scheme
    [InlineData("not-a-url")] // invalid URL
    public async Task GetOnboardingLink_Should_Reject_Invalid_URLs(string invalidUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var validUrl = "https://example.com/valid";

        _client.SetTestAuth(sellerId.ToString(), ["User"]);

        // Act
        var response = await _client.PostAsync(
            $"/api/v1/sellers/{sellerId}/onboarding-link?returnUrl={Uri.EscapeDataString(invalidUrl)}&refreshUrl={Uri.EscapeDataString(validUrl)}",
            null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Invalid redirect URL");
    }

    [Fact]
    public async Task GetOnboardingLink_Should_Return_Forbidden_For_Wrong_User()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var wrongUserId = Guid.NewGuid();

        _client.SetTestAuth(wrongUserId.ToString(), ["User"]);

        // Act
        var response = await _client.PostAsync($"/api/v1/sellers/{sellerId}/onboarding-link?returnUrl=https://example.com&refreshUrl=https://example.com", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SimulateTransaction_Demo_Should_Create_Ledger_Entries()
    {
        // Arrange
        var request = new { AmountCents = 5000L, Currency = "USD" };

        _client.SetTestAuth(Guid.NewGuid().ToString(), ["Service"]);

        // Act
        var response = await _client.PostAsJsonAsync("/demo/ledger/simulate", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content);

        result.GetProperty("balanceCents").GetInt64().Should().Be(4500L); // 5000 - 10% commission
        result.GetProperty("entries").EnumerateArray().Should().HaveCount(2);

        var entries = result.GetProperty("entries").EnumerateArray().ToList();
        entries[0].GetProperty("type").GetString().Should().Be("credit");
        entries[0].GetProperty("amountCents").GetInt64().Should().Be(5000L);
        entries[1].GetProperty("type").GetString().Should().Be("debit");
        entries[1].GetProperty("amountCents").GetInt64().Should().Be(500L); // 10% commission
    }
}