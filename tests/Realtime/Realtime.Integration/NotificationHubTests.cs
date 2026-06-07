using FluentAssertions;
using Haworks.Realtime.Api.Application.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Haworks.BuildingBlocks.Testing;
using Haworks.BuildingBlocks.Testing.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

namespace Haworks.Realtime.Integration;

/// <summary>
/// Custom test authentication handler that allows setting a specific user ID per test
/// instead of using the fixed "test-user" from TestAuthenticationHandler.
/// </summary>
public sealed class CustomTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CustomTest";

    // Static property to set user ID for tests
    public static string? CurrentTestUserId { get; set; }

    public CustomTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = CurrentTestUserId ?? "default-test-user";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(ClaimTypes.Role, "User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

[Collection("Realtime Integration")]
public class NotificationHubTests : IAsyncLifetime
{
    private readonly RealtimeWebAppFactory _factory;
    private readonly Guid _testUserId = Guid.NewGuid();

    public NotificationHubTests(RealtimeWebAppFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => _factory.ResetRedisAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OnConnectedAsync_Should_FlushPendingMessages()
    {
        // Arrange - Pre-populate inbox with test messages
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();
        var message1Id = Guid.NewGuid();
        var message2Id = Guid.NewGuid();

        await inboxService.StoreMessageAsync(_testUserId, message1Id, "OrderStatusChanged",
            new { OrderId = "order-1", Status = "Shipped" }, CancellationToken.None);
        await inboxService.StoreMessageAsync(_testUserId, message2Id, "PaymentCompleted",
            new { PaymentId = "payment-1", Amount = 99.99 }, CancellationToken.None);

        // Verify messages are in inbox
        var pendingMessages = await inboxService.GetMessagesAsync(_testUserId);
        pendingMessages.Should().HaveCount(2);

        // Set the user ID for the custom auth handler
        CustomTestAuthHandler.CurrentTestUserId = _testUserId.ToString();

        // Create factory with custom auth handler for this specific user
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(CustomTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CustomTestAuthHandler>(
                        CustomTestAuthHandler.SchemeName,
                        _ => { });
            });
        });

        // Set up SignalR connection - no need for token since we're using custom auth
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{customFactory.Server.BaseAddress}hubs/notifications", options =>
            {
                // Configure for test environment
                options.HttpMessageHandlerFactory = _ => customFactory.Server.CreateHandler();
            })
            .Build();

        var receivedMessages = new List<InboxMessage>();
        hubConnection.On<InboxMessage>("ReceiveNotification", message =>
        {
            receivedMessages.Add(message);
        });

        try
        {
            // Act - Connect to hub (triggers OnConnectedAsync)
            await hubConnection.StartAsync();

            // Wait for messages to be received
            await TestWait.Until(() => receivedMessages.Count == 2, TimeSpan.FromSeconds(10));

            // Assert - Messages were pushed via SignalR
            receivedMessages.Should().HaveCount(2);

            // Verify message contents
            var messageIds = receivedMessages.Select(m => m.MessageId).ToList();
            var messageTypes = receivedMessages.Select(m => m.MessageType).ToList();

            messageIds.Should().Contain(message1Id);
            messageIds.Should().Contain(message2Id);
            messageTypes.Should().Contain("OrderStatusChanged");
            messageTypes.Should().Contain("PaymentCompleted");

            // Assert - Messages were acknowledged/removed from inbox
            await TestWait.Until(async () =>
            {
                var remainingMessages = await inboxService.GetMessagesAsync(_testUserId);
                return remainingMessages.Count == 0;
            }, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await hubConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task OnConnectedAsync_Should_SkipFlush_When_NoMessagesInInbox()
    {
        // Arrange - Empty inbox
        var inboxService = _factory.Services.GetRequiredService<IInboxService>();
        var pendingMessages = await inboxService.GetMessagesAsync(_testUserId);
        pendingMessages.Should().BeEmpty();

        // Set the user ID for the custom auth handler
        CustomTestAuthHandler.CurrentTestUserId = _testUserId.ToString();

        // Create factory with custom auth handler
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(CustomTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CustomTestAuthHandler>(
                        CustomTestAuthHandler.SchemeName,
                        _ => { });
            });
        });

        // Set up SignalR connection
        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{customFactory.Server.BaseAddress}hubs/notifications", options =>
            {
                // Configure for test environment
                options.HttpMessageHandlerFactory = _ => customFactory.Server.CreateHandler();
            })
            .Build();

        var receivedMessages = new List<InboxMessage>();
        hubConnection.On<InboxMessage>("ReceiveNotification", message =>
        {
            receivedMessages.Add(message);
        });

        try
        {
            // Act - Connect to hub
            await hubConnection.StartAsync();

            // Wait a bit to ensure no messages are sent
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Assert - No messages were received (inbox was empty)
            receivedMessages.Should().BeEmpty();
        }
        finally
        {
            await hubConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task OnConnectedAsync_Should_HandleInvalidUserId_Gracefully()
    {
        // Arrange - Create factory with invalid user ID (not a GUID)
        var invalidUserId = "invalid-user-id";

        // Set the invalid user ID for the custom auth handler
        CustomTestAuthHandler.CurrentTestUserId = invalidUserId;

        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(CustomTestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CustomTestAuthHandler>(
                        CustomTestAuthHandler.SchemeName,
                        _ => { });
            });
        });

        var hubConnection = new HubConnectionBuilder()
            .WithUrl($"{customFactory.Server.BaseAddress}hubs/notifications", options =>
            {
                // Configure for test environment
                options.HttpMessageHandlerFactory = _ => customFactory.Server.CreateHandler();
            })
            .Build();

        var receivedMessages = new List<InboxMessage>();
        hubConnection.On<InboxMessage>("ReceiveNotification", message =>
        {
            receivedMessages.Add(message);
        });

        try
        {
            // Act - Connect to hub with invalid user ID
            await hubConnection.StartAsync();

            // Wait a bit
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Assert - Connection should succeed but no messages flushed due to invalid user ID
            receivedMessages.Should().BeEmpty();
            hubConnection.State.Should().Be(HubConnectionState.Connected);
        }
        finally
        {
            await hubConnection.DisposeAsync();
        }
    }
}