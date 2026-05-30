using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace Haworks.Payouts.Integration.Controllers;

[Collection(nameof(PayoutsIntegrationTestDefinition))]
public class DemoLedgerControllerIntegrationTests : IAsyncLifetime
{
    private readonly PayoutsWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;
    private readonly PayoutsDbContext _db;

    public DemoLedgerControllerIntegrationTests(PayoutsWebAppFactory factory)
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
    public async Task SimulateTransaction_Should_Create_Account_And_Entries_Successfully()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", adminId.ToString());

        var request = new
        {
            AmountCents = 5000L,
            Currency = "USD"
        };

        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/demo/ledger/simulate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        // Verify response structure
        Assert.NotNull(result!.sellerId);
        Assert.NotNull(result.accountId);
        Assert.Equal(4500L, (long)result.balanceCents); // 5000 - 10% commission = 4500
        Assert.NotNull(result.entries);

        // Verify database entries
        var sellerId = Guid.Parse((string)result.sellerId);
        var account = await _db.LedgerAccounts.FirstAsync(a => a.OwnerId == sellerId);
        account.BalanceCents.Should().Be(4500L);

        var entries = await _db.LedgerEntries.Where(e => e.AccountId == account.Id).ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Type == Domain.Enums.EntryType.Credit && e.AmountCents == 5000L);
        entries.Should().Contain(e => e.Type == Domain.Enums.EntryType.Debit && e.AmountCents == 500L);
    }

    [Fact]
    public async Task SimulateTransaction_Should_Use_Default_Currency_When_Not_Specified()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", adminId.ToString());

        var request = new
        {
            AmountCents = 3999L
        };

        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/demo/ledger/simulate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        // Verify default values
        var sellerId = Guid.Parse((string)result!.sellerId);
        var account = await _db.LedgerAccounts.FirstAsync(a => a.OwnerId == sellerId);
        account.Currency.Should().Be("USD");
        account.Type.Should().Be(Domain.Enums.AccountType.SellerPayable);
    }

    [Fact]
    public async Task SimulateTransaction_Should_Return_Unauthorized_When_No_Auth_Header()
    {
        // Arrange
        var request = new
        {
            AmountCents = 1000L,
            Currency = "USD"
        };

        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/demo/ledger/simulate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SimulateTransaction_Should_Return_Forbidden_When_Not_Admin_Or_Service()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", sellerId.ToString());

        var request = new
        {
            AmountCents = 1000L,
            Currency = "USD"
        };

        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/demo/ledger/simulate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SimulateTransaction_Should_Allow_Service_Role()
    {
        // Arrange
        var serviceId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test");
        _client.DefaultRequestHeaders.Add("X-User-Id", serviceId.ToString());

        var request = new
        {
            AmountCents = 2000L,
            Currency = "EUR"
        };

        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/demo/ledger/simulate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        var sellerId = Guid.Parse((string)result!.sellerId);
        var account = await _db.LedgerAccounts.FirstAsync(a => a.OwnerId == sellerId);
        account.Currency.Should().Be("EUR");
        account.BalanceCents.Should().Be(1800L); // 2000 - 10% = 1800
    }
}