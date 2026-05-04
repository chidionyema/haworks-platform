using Haworks.BuildingBlocks.Common;

namespace Haworks.Orders.Domain.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetBySagaIdTrackedAsync(Guid sagaId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> ListByUserAsync(string userId, int skip, int take, CancellationToken ct = default);
    Task<int> CountByUserAsync(string userId, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Guest order methods
    Task AddGuestInfoAsync(GuestOrderInfo guestInfo, CancellationToken ct = default);
    Task<GuestOrderInfo?> GetGuestInfoAsync(Guid orderId, CancellationToken ct = default);
    Task<GuestOrderInfo?> GetGuestByTokenAsync(string token, CancellationToken ct = default);
    
    // Recovery / Stock Janitor methods
    Task<IReadOnlyList<Order>> GetAbandonedOrdersAsync(DateTime cutoffTime, int take = 100, CancellationToken ct = default);
    Task<bool> MarkStockReleasedAsync(Guid orderId, OrderStatus newStatus, string reason, CancellationToken ct = default);
}
