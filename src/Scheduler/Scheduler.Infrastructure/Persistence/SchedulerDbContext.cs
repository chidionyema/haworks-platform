using MassTransit;
using Microsoft.EntityFrameworkCore;
using Haworks.Scheduler.Application.Common.Interfaces;
using Haworks.Scheduler.Domain.Entities;

namespace Haworks.Scheduler.Infrastructure.Persistence;

public class SchedulerDbContext : DbContext
{
    public SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : base(options) { }

    public DbSet<VaultLease> VaultLeases => Set<VaultLease>();
    public DbSet<RotationAuditEntry> RotationAuditEntries => Set<RotationAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("scheduler");
        builder.AddInboxStateEntity();
        builder.AddOutboxStateEntity();
        builder.AddOutboxMessageEntity();

        builder.Entity<VaultLease>(entity =>
        {
            entity.HasKey(e => e.Id);

            // xmin concurrency token (Postgres optimistic concurrency)
            entity.Property(e => e.xmin)
                .IsRowVersion();

            entity.Property(e => e.ServiceName).IsRequired().HasMaxLength(128);
            entity.Property(e => e.RoleName).IsRequired().HasMaxLength(128);
            entity.Property(e => e.CredentialType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.LeaseId).HasMaxLength(512);
            entity.Property(e => e.LastError).HasMaxLength(2048);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            // Unique constraint: one lease per service/role/type triple
            entity.HasIndex(e => new { e.ServiceName, e.RoleName, e.CredentialType })
                .IsUnique();

            // Index for watcher job's primary query path
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });
        });

        builder.Entity<RotationAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Action).IsRequired().HasMaxLength(32);
            entity.Property(e => e.NewLeaseId).HasMaxLength(512);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);

            entity.HasIndex(e => e.LeaseId);
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
