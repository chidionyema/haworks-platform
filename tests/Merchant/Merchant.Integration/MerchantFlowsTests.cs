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
    public async Task GetBySlug_returns_merchant()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var slug = $"test-slug-{Guid.NewGuid():N}";
        var command = new { OwnerId = ownerId, Name = "Slug Test", Slug = slug };

        await _client.PostAsJsonAsync("/api/v1/merchants", command);

        // Act
        var response = await _client.GetAsync($"/api/v1/merchants/by-slug/{slug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = await response.Content.ReadFromJsonAsync<MerchantDto>();
        merchant.Should().NotBeNull();
        merchant!.Slug.Should().Be(slug);
    }

    [Fact]
    public async Task GetBySlug_nonexistent_returns_404()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/merchants/by-slug/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMine_returns_merchant_for_owner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "My Merchant", Slug = $"my-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync("/api/v1/merchants", command);

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        // Act
        var response = await _client.GetAsync("/api/v1/merchants/mine");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchant = await response.Content.ReadFromJsonAsync<MerchantDto>();
        merchant.Should().NotBeNull();
        merchant!.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public async Task GetMine_no_merchant_returns_404()
    {
        // Arrange
        var nonOwnerUserId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", nonOwnerUserId.ToString());

        // Act
        var response = await _client.GetAsync("/api/v1/merchants/mine");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_requires_admin_role()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/merchants");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_with_admin_role_returns_merchants()
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        // Create a test merchant
        var command = new { OwnerId = Guid.NewGuid(), Name = "List Test", Slug = $"list-{Guid.NewGuid():N}" };
        await _client.PostAsJsonAsync("/api/v1/merchants", command);

        // Act
        var response = await _client.GetAsync("/api/v1/merchants");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var merchants = await response.Content.ReadFromJsonAsync<MerchantDto[]>();
        merchants.Should().NotBeNull();
        merchants!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Update_unauthorized_user_returns_403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Update Test", Slug = $"update-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        var differentUserId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", differentUserId.ToString());

        var updateRequest = new { Name = "Updated Name" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_valid_owner_updates_successfully()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Original Name", Slug = $"update-valid-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        var updateRequest = new { Name = "Updated Name", Bio = "New Bio" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update
        var getResp = await _client.GetAsync($"/api/v1/merchants/{createResult.MerchantId}");
        var merchant = await getResp.Content.ReadFromJsonAsync<MerchantDto>();
        merchant!.Name.Should().Be("Updated Name");
        merchant.Bio.Should().Be("New Bio");
    }

    [Fact]
    public async Task SetHours_unauthorized_user_returns_403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Hours Test", Slug = $"hours-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        var differentUserId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", differentUserId.ToString());

        var hours = new[] { new { DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" } };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/hours", hours);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SetHours_valid_owner_updates_successfully()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Hours Valid", Slug = $"hours-valid-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        var hours = new[] { new { DayOfWeek = 1, OpenTime = "09:00", CloseTime = "17:00" } };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/hours", hours);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Approve_requires_admin_role()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Approve Test", Slug = $"approve-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approve_with_admin_role_succeeds()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Approve Admin", Slug = $"approve-admin-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", "admin-1");

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/approve", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reject_requires_admin_role()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Reject Test", Slug = $"reject-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        var rejectRequest = new { Reason = "Test rejection" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/reject", rejectRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reject_with_admin_role_succeeds()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Reject Admin", Slug = $"reject-admin-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", "admin-1");

        var rejectRequest = new { Reason = "Admin rejection" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/reject", rejectRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Suspend_requires_admin_role()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Suspend Test", Slug = $"suspend-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        var suspendRequest = new { Reason = "Test suspension" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/suspend", suspendRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Suspend_with_admin_role_succeeds()
    {
        // Arrange
        var command = new { OwnerId = Guid.NewGuid(), Name = "Suspend Admin", Slug = $"suspend-admin-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", "admin-1");

        var suspendRequest = new { Reason = "Admin suspension" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/merchants/{createResult!.MerchantId}/suspend", suspendRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deactivate_unauthorized_user_returns_403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Deactivate Test", Slug = $"deactivate-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        var differentUserId = Guid.NewGuid();
        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", differentUserId.ToString());

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deactivate_valid_owner_succeeds()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var command = new { OwnerId = ownerId, Name = "Deactivate Valid", Slug = $"deactivate-valid-{Guid.NewGuid():N}" };
        var createResp = await _client.PostAsJsonAsync("/api/v1/merchants", command);
        var createResult = await createResp.Content.ReadFromJsonAsync<CreateResult>();

        _client.DefaultRequestHeaders.Add("X-Forwarded-User-Id", ownerId.ToString());

        // Act
        var response = await _client.PostAsync($"/api/v1/merchants/{createResult!.MerchantId}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private record CreateResult(Guid MerchantId);
}
