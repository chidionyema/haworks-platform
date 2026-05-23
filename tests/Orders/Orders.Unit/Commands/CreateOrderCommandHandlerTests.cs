using Haworks.Orders.Application.Commands;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using Haworks.BuildingBlocks.Testing;
using Haworks.Contracts.Orders;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Orders.UnitTests.Commands;

public class CreateOrderCommandHandlerTests : TestBase
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly Mock<IPublishEndpoint> _eventPublisherMock;
    private readonly CreateOrderCommandHandler _handler;

    public CreateOrderCommandHandlerTests(ITestOutputHelper output) : base(output)
    {
        _orderRepositoryMock = MockRepository.Create<IOrderRepository>();
        _eventPublisherMock = MockRepository.Create<IPublishEndpoint>();
        var loggerMock = new Mock<ILogger<CreateOrderCommandHandler>>();

        _handler = new CreateOrderCommandHandler(
            _orderRepositoryMock.Object,
            _eventPublisherMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesOrderAndPublishesEvent()
    {
        // Arrange
        var command = new CreateOrderCommand(
            Guid.NewGuid().ToString(),
            "test@example.com",
            10_000L,
            "USD",
            Guid.NewGuid(),
            "idempotency-key",
            new List<CreateOrderLineItem>
            {
                new(Guid.NewGuid(), "Product 1", 1, 10_000L)
            });

        _orderRepositoryMock
            .Setup(x => x.GetBySagaIdTrackedAsync(command.SagaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        _orderRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisherMock
            .Setup(x => x.Publish(It.IsAny<OrderCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _orderRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _orderRepositoryMock.Verify(
            x => x.AddAsync(
                It.Is<Order>(o =>
                    o.UserId == command.UserId &&
                    o.SagaId == command.SagaId &&
                    o.IdempotencyKey == command.IdempotencyKey &&
                    o.CustomerEmail == command.CustomerEmail &&
                    o.TotalAmountCents == command.TotalAmountCents &&
                    o.Items.Count == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisherMock.Verify(
            x => x.Publish(
                It.Is<OrderCreatedEvent>(e =>
                    e.CustomerEmail == command.CustomerEmail &&
                    e.TotalAmountCents == command.TotalAmountCents),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
