using Haworks.Webhooks.Domain;
using Haworks.Webhooks.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Webhooks.Infrastructure.Persistence;

public sealed class WebhooksDbContext(DbContextOptions<WebhooksDbContext> options) : DbContext(options), IWebhooksDbContext
{
    public DbSet<WebhookSubscription> Subscriptions => Set<WebhookSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("webhooks");

        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            entity.ToTable("webhook_subscriptions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PartnerId).HasColumnType("uuid").IsRequired();
            entity.Property(e => e.Url).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Secret).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SecretHash).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SecretPreview).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Events).IsRequired();

            entity.HasIndex(e => e.PartnerId).HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Events).HasMethod("gin");
        });
    }
}
