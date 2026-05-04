using Haworks.Orders.Application.Queries;
using Haworks.Orders.Application.DTOs;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using Haworks.BuildingBlocks.Testing;
using Haworks.Orders.UnitTests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using Xunit;

namespace Haworks.Orders.UnitTests.Queries;

public class GetOrderByIdQueryHandlerTests : TestBase
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock;
    private readonly GetOrderByIdQueryHandler _handler;

    public GetOrderByIdQueryHandlerTests(ITestOutputHelper output) : base(output)
    {
        _orderRepositoryMock = MockRepository.Create<IOrderRepository>();
        _handler = new GetOrderByIdQueryHandler(_orderRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOrderExists_ReturnsOrder()
    {
        var order = OrderTestHelpers.CreateOrder();
        _orderRepositoryMock.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _handler.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(order.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ReturnsFailure()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _handler.Handle(new GetOrderByIdQuery(orderId), CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
