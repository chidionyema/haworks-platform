using Haworks.BuildingBlocks.Messaging;
using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Domain.Aggregates;
using Haworks.Privacy.Application.Requests.Sagas;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace Haworks.Privacy.Infrastructure.Persistence;

public class PrivacyDbContext : DbContext, IPrivacyDbContext
{
    public PrivacyDbContext(DbContextOptions<PrivacyDbContext> options) : base(options) { }

    public DbSet<PrivacyRequest> PrivacyRequests => Set<PrivacyRequest>();
    public DbSet<PrivacyRequestStep> PrivacyRequestSteps => Set<PrivacyRequestStep>();
    public DbSet<SagaTransitionAuditEntry> SagaTransitionAudit => Set<SagaTransitionAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("privacy");

        builder.AddInboxStateEntity();
        builder.AddOutboxStateEntity();
        builder.AddOutboxMessageEntity();

        builder.Entity<PrivacyRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            // Concurrency handled by domain guards + pessimistic locks.
        });

        builder.Entity<PrivacyRequestStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RequestId);
            // Concurrency handled by domain guards + pessimistic locks.
        });

        builder.Entity<PrivacyRequestState>(entity =>
        {
            entity.HasKey(e => e.CorrelationId);
            entity.Property(e => e.CurrentState).HasMaxLength(64);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();

            // H9: per-service completion timestamps for GDPR audit trail
            entity.Property(e => e.IdentityCompletedAt);
            entity.Property(e => e.OrdersCompletedAt);
            entity.Property(e => e.PaymentsCompletedAt);

            // C2: comma-separated failed services
            entity.Property(e => e.FailedServices).HasMaxLength(256);

            // FailedServicesSet is a computed property — not persisted
            entity.Ignore(e => e.FailedServicesSet);
        });

        builder.Entity<SagaTransitionAuditEntry>(e =>
        {
            e.ToTable("SagaTransitionAudit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.InitiatedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.SagaType, x.CorrelationId });
        });
    }
}
