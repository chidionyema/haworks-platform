using FluentAssertions;
using Haworks.Orders.Application.Queries;
using Haworks.Orders.Domain;
using Haworks.Orders.Domain.Interfaces;
using Moq;
using Xunit;

namespace Haworks.Orders.Unit.Queries;

public class OrderQueryHandlerTests
{
    private readonly Mock<IOrderRepository> _repositoryMock = new();
    private readonly GetOrderByIdQueryHandler _getByIdHandler;
    private readonly ListUserOrdersQueryHandler _listByUserHandler;

    public OrderQueryHandlerTests()
    {
        _getByIdHandler = new GetOrderByIdQueryHandler(_repositoryMock.Object);
        _listByUserHandler = new ListUserOrdersQueryHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetOrderById_WhenExists_ReturnsSuccess()
    {
        var orderId = Guid.NewGuid();
        var order = Order.Create("user-123", 10_000L, "USD", Guid.NewGuid(), "key", "email",
            new List<(Guid, string, int, long)> { (Guid.NewGuid(), "P1", 1, 10_000L) });

        _repositoryMock.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await _getByIdHandler.Handle(new GetOrderByIdQuery(orderId, "user-123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalAmountCents.Should().Be(10_000L);
    }

    [Fact]
    public async Task ListUserOrders_WithValidUser_ReturnsPagedResult()
    {
        var userId = "user-123";
        var order = Order.Create(userId, 10_000L, "USD", Guid.NewGuid(), "key", "email",
            new List<(Guid, string, int, long)> { (Guid.NewGuid(), "P1", 1, 10_000L) });
        
        _repositoryMock.Setup(r => r.ListByUserAsync(userId, 0, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Order> { order });
        _repositoryMock.Setup(r => r.CountByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _listByUserHandler.Handle(new ListUserOrdersQuery(userId, 0, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Total.Should().Be(1);
    }

    [Fact]
    public async Task ListUserOrders_WithEmptyUserId_ReturnsFailure()
    {
        var result = await _listByUserHandler.Handle(new ListUserOrdersQuery("", 0, 20), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
    }
}
