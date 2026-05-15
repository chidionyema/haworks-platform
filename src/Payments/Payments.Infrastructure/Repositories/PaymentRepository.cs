using Microsoft.EntityFrameworkCore;
using Haworks.Payments.Domain;
using Haworks.Payments.Domain.Interfaces;
using Haworks.Contracts.Payments;

namespace Haworks.Payments.Infrastructure.Repositories;

internal sealed class PaymentRepository(PaymentDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> GetByProviderSessionAsync(PaymentProvider provider, string providerSessionId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Provider == provider && p.ProviderSessionId == providerSessionId, ct);

    public Task<Payment?> GetByProviderSessionTrackedAsync(PaymentProvider provider, string providerSessionId, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.Provider == provider && p.ProviderSessionId == providerSessionId, ct);

    public Task<Payment?> GetByOrderIdTrackedAsync(Guid orderId, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

    public Task<Payment?> GetByProviderTransactionIdAsync(string providerTransactionId, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.ProviderTransactionId == providerTransactionId, ct);

    public async Task<IReadOnlyList<Payment>> ListByUserAsync(string userId, CancellationToken ct = default) =>
        await db.Payments.Where(p => p.UserId == userId).ToListAsync(ct);

    public async Task AddAsync(Payment payment, CancellationToken ct = default) =>
        await db.Payments.AddAsync(payment, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    // Subscription methods
    public Task<Subscription?> GetSubscriptionByProviderIdAsync(string providerSubscriptionId, CancellationToken ct = default) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.ProviderSubscriptionId == providerSubscriptionId, ct);

    public Task<Subscription?> GetSubscriptionByUserIdAsync(string userId, CancellationToken ct = default) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task AddSubscriptionAsync(Subscription subscription, CancellationToken ct = default) =>
        await db.Subscriptions.AddAsync(subscription, ct);

    // WebhookEvent methods
    public async Task AddWebhookEventAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await db.WebhookEvents.AddAsync(webhookEvent, ct);

    public Task<bool> WebhookEventExistsAsync(PaymentProvider provider, string providerEventId, CancellationToken ct = default) =>
        db.WebhookEvents.AnyAsync(w => w.Provider == provider && w.ProviderEventId == providerEventId, ct);
}
