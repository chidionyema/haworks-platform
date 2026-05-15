using Haworks.Payments.Domain;
using Haworks.Contracts.Payments;

namespace Haworks.Payments.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetByProviderSessionAsync(PaymentProvider provider, string providerSessionId, CancellationToken ct = default);
    Task<Payment?> GetByProviderSessionTrackedAsync(PaymentProvider provider, string providerSessionId, CancellationToken ct = default);
    Task<Payment?> GetByOrderIdTrackedAsync(Guid orderId, CancellationToken ct = default);
    Task<Payment?> GetByProviderTransactionIdAsync(string providerTransactionId, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> ListByUserAsync(string userId, CancellationToken ct = default);
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Subscription methods
    Task<Subscription?> GetSubscriptionByProviderIdAsync(string providerSubscriptionId, CancellationToken ct = default);
    Task<Subscription?> GetSubscriptionByUserIdAsync(string userId, CancellationToken ct = default);
    Task AddSubscriptionAsync(Subscription subscription, CancellationToken ct = default);

    // WebhookEvent methods
    Task AddWebhookEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
    Task<bool> WebhookEventExistsAsync(PaymentProvider provider, string providerEventId, CancellationToken ct = default);
}
