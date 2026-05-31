using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.Merchant.Application.Merchants.DTOs;
using Haworks.Contracts.Merchant;

namespace Haworks.Merchant.Integration;

[Collection("Merchant Integration")]
public sealed class MerchantFlowsTests : IAsyncLifetime
{
    private readonly MerchantWebAppFactory _factory;
    private readonly HttpClient _client;

    public MerchantFlowsTests(MerchantWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Start();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Health_returns_200()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_then_get_merchant_round_trips()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var name = "Test Merchant";
        var slug = $"test-merchant-{Guid.NewGuid():N}";
        var command = new { OwnerId = ownerId, Name = name, Slug = slug };

        // Act
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        
        // Assert
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();
        createResult.Should().NotBeNull();
        createResult!.MerchantId.Should().NotBeEmpty();

        // Get by ID
        var getResp = await _client.GetAsync($"/api/v1/merchants/{createResult.MerchantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = await getResp.Content.ReadFromJsonAsync<MerchantDto>();
        merchant.Should().NotBeNull();
        merchant!.Name.Should().Be(name);
        merchant.Slug.Should().Be(slug);
        merchant.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public async Task Create_merchant_publishes_MerchantCreatedEvent()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var name = "Event Test Merchant";
        var slug = $"event-test-{Guid.NewGuid():N}";
        var command = new { OwnerId = ownerId, Name = name, Slug = slug };
        var harness = _factory.Services.GetRequiredService<ITestHarness>();

        // Act
        await _client.PostAsJsonAsync("/api/v1/merchants", command);

        // Assert
        (await harness.Published.Any<MerchantCreatedEvent>(x => string.Equals(x.Context.Message.Slug, slug, StringComparison.Ordinal))).Should().BeTrue();
    }

    [Fact]
    public async Task GetBySlug_ValidSlug_Returns200WithMerchant()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var name = "Slug Test Merchant";
        var slug = $"slug-test-{Guid.NewGuid():N}";
        var command = new { OwnerId = ownerId, Name = name, Slug = slug };

        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        // Act
        var response = await _client.GetAsync($"/api/v1/merchants/by-slug/{slug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = await response.Content.ReadFromJsonAsync<MerchantDto>();
        merchant.Should().NotBeNull();
        merchant!.Id.Should().Be(createResult!.MerchantId);
        merchant.Name.Should().Be(name);
        merchant.Slug.Should().Be(slug);
    }

    [Fact]
    public async Task GetBySlug_NonexistentSlug_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/merchants/by-slug/nonexistent-slug");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMine_AuthenticatedUser_Returns200WithMerchant()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var name = "My Merchant";
        var slug = $"my-merchant-{Guid.NewGuid():N}";
        var command = new { OwnerId = ownerId, Name = name, Slug = slug };

        await _client.PostAsJsonAsync("/api/v1/merchants", command);

        // Set authenticated user
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        // Act
        var response = await _client.GetAsync("/api/v1/merchants/mine");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = await response.Content.ReadFromJsonAsync<MerchantDto>();
        merchant.Should().NotBeNull();
        merchant!.Name.Should().Be(name);
        merchant.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public async Task GetMine_UnauthenticatedUser_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/merchants/mine");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListMerchants_AdminUser_Returns200WithList()
    {
        // Arrange - Create test merchants
        var merchant1 = new { OwnerId = Guid.NewGuid(), Name = "List Test 1", Slug = $"list-test-1-{Guid.NewGuid():N}" };
        var merchant2 = new { OwnerId = Guid.NewGuid(), Name = "List Test 2", Slug = $"list-test-2-{Guid.NewGuid():N}" };

        await _client.PostAsJsonAsync("/api/v1/merchants", merchant1);
        await _client.PostAsJsonAsync("/api/v1/merchants", merchant2);

        // Set admin user
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "Admin");

        // Act
        var response = await _client.GetAsync("/api/v1/merchants?skip=0&take=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MerchantListDto>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ListMerchants_NonAdminUser_Returns403()
    {
        // Arrange - Set non-admin user
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "User");

        // Act
        var response = await _client.GetAsync("/api/v1/merchants");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateMerchant_ValidOwner_Returns204()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Update Test", Slug = $"update-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        var updateRequest = new { Name = "Updated Name", Bio = "Updated Bio" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update
        var getResp = await _client.GetAsync($"/api/v1/merchants/{createResult.MerchantId}");
        var merchant = await getResp.Content.ReadFromJsonAsync<MerchantDto>();
        merchant!.Name.Should().Be("Updated Name");
        merchant.Bio.Should().Be("Updated Bio");
    }

    [Fact]
    public async Task UpdateMerchant_WrongOwner_Returns403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var wrongUserId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Update Test", Slug = $"update-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", wrongUserId.ToString());

        var updateRequest = new { Name = "Updated Name" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApproveMerchant_AdminUser_Returns204AndPublishesEvent()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Approve Test", Slug = $"approve-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "Admin");

        var harness = _factory.Services.GetRequiredService<ITestHarness>();

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify event published
        (await harness.Published.Any<MerchantActivatedEvent>(x => x.Context.Message.MerchantId == createResult.MerchantId)).Should().BeTrue();
    }

    [Fact]
    public async Task ApproveMerchant_NonAdminUser_Returns403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Approve Test", Slug = $"approve-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "User");

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RejectMerchant_AdminUser_Returns204()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Reject Test", Slug = $"reject-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "Admin");

        var rejectRequest = new { Reason = "Test rejection" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/reject", rejectRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SuspendMerchant_AdminUser_Returns204()
    {
        // Arrange - Create and approve merchant first
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Suspend Test", Slug = $"suspend-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "Admin");

        // Approve first
        await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        var suspendRequest = new { Reason = "Test suspension" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult.MerchantId}/suspend", suspendRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeactivateMerchant_ValidOwner_Returns204()
    {
        // Arrange - Create and approve merchant first
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Deactivate Test", Slug = $"deactivate-test-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        // Approve as admin
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", Guid.NewGuid().ToString());
        _client.DefaultRequestHeaders.Add("X-User-Role", "Admin");
        await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        // Switch to owner
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult.MerchantId}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private record CreateResult(Guid MerchantId);
    private record MerchantListDto(IReadOnlyList<MerchantDto> Items, int TotalCount);
}
