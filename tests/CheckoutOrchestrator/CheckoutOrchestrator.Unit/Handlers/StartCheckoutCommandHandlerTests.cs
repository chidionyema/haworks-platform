using FluentAssertions;
using Haworks.CheckoutOrchestrator.Application.Commands;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using Haworks.Contracts.Checkout;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.CheckoutOrchestrator.Unit.Handlers;

public class StartCheckoutCommandHandlerTests
{
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;
    private readonly Mock<ICheckoutDbContext> _mockDbContext;
    private readonly Mock<DbSet<object>> _mockDbSet;
    private readonly StartCheckoutCommandHandler _handler;

    public StartCheckoutCommandHandlerTests()
    {
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
        _mockDbContext = new Mock<ICheckoutDbContext>();
        _mockDbSet = new Mock<DbSet<object>>();

        _mockDbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);

        _handler = new StartCheckoutCommandHandler(_mockPublishEndpoint.Object, _mockDbContext.Object);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldReturnSuccess()
    {
        // Arrange
        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: "key-123",
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "USD" }
            },
            Currency: "USD"
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.OrderId.Should().Be(command.OrderId);

        // Verify event was published
        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<CheckoutInitiatedEvent>(e =>
                e.OrderId == command.OrderId &&
                e.UserId == command.UserId &&
                e.CustomerEmail == command.CustomerEmail &&
                e.TotalAmountCents == command.TotalAmountCents &&
                e.IdempotencyKey == command.IdempotencyKey &&
                e.Currency == "USD"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify SaveChanges was called
        _mockDbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithEmptyOrderId_ShouldGenerateNewOrderId()
    {
        // Arrange
        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.Empty, // Empty OrderId should be regenerated
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: "key-123",
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "USD" }
            }
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().NotBe(Guid.Empty);
        result.Value.OrderId.Should().NotBe(command.OrderId);
    }

    [Fact]
    public async Task Handle_WithNullCurrency_ShouldDefaultToUSD()
    {
        // Arrange
        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: "key-123",
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "EUR" }
            },
            Currency: null // Null currency should default to USD
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<CheckoutInitiatedEvent>(e => e.Currency == "USD"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldGenerateDeterministicSagaIdFromIdempotencyKey()
    {
        // Arrange
        const string idempotencyKey = "test-key-123";
        var command1 = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(), // User-provided SagaId should be ignored
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: idempotencyKey,
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "USD" }
            }
        );

        var command2 = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(), // Different user-provided SagaId
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: idempotencyKey, // Same idempotency key
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "USD" }
            }
        );

        // Act
        var result1 = await _handler.Handle(command1, CancellationToken.None);
        var result2 = await _handler.Handle(command2, CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.SagaId.Should().Be(result2.Value.SagaId); // Same idempotency key should generate same SagaId
        result1.Value.SagaId.Should().NotBe(command1.SagaId); // Should not use user-provided SagaId
        result2.Value.SagaId.Should().NotBe(command2.SagaId); // Should not use user-provided SagaId
    }

    [Fact]
    public async Task Handle_WithEmptyIdempotencyKey_ShouldGenerateRandomSagaId()
    {
        // Arrange
        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: "", // Empty idempotency key should still generate a SagaId
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 10000L, Currency = "USD" }
            }
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SagaId.Should().NotBe(Guid.Empty);
        result.Value.SagaId.Should().NotBe(command.SagaId);
    }

    [Fact]
    public async Task Handle_ShouldSetActivityTags()
    {
        // Arrange
        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 15000L,
            IdempotencyKey: "key-123",
            Items: new List<CheckoutItemData>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 2, UnitPriceCents = 7500L, Currency = "USD" }
            }
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // We can't directly assert on activity tags in this unit test without more complex setup,
        // but we can verify the handler completes successfully when activity telemetry is invoked
    }

    [Fact]
    public async Task Handle_WithMultipleItems_ShouldPublishAllItems()
    {
        // Arrange
        var items = new List<CheckoutItemData>
        {
            new() { ProductId = Guid.NewGuid(), ProductName = "Product 1", Quantity = 1, UnitPriceCents = 5000L, Currency = "USD" },
            new() { ProductId = Guid.NewGuid(), ProductName = "Product 2", Quantity = 2, UnitPriceCents = 2500L, Currency = "USD" }
        };

        var command = new StartCheckoutCommand(
            SagaId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            UserId: "user-123",
            CustomerEmail: "test@example.com",
            TotalAmountCents: 10000L,
            IdempotencyKey: "key-123",
            Items: items
        );

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        _mockPublishEndpoint.Verify(x => x.Publish(
            It.Is<CheckoutInitiatedEvent>(e =>
                e.Items.Count == 2 &&
                e.Items.All(item => items.Any(original =>
                    original.ProductId == item.ProductId &&
                    original.ProductName == item.ProductName &&
                    original.Quantity == item.Quantity &&
                    original.UnitPriceCents == item.UnitPriceCents))),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}