using Haworks.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Haworks.Webhooks.Application.Interfaces;

public interface IWebhooksDbContext
{
    DbSet<WebhookSubscription> Subscriptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    ChangeTracker ChangeTracker { get; }
}
