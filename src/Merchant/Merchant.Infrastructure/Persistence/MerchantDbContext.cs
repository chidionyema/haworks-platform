using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Domain.Aggregates;
using Haworks.Merchant.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace Haworks.Merchant.Infrastructure.Persistence;

public class MerchantDbContext : DbContext, IMerchantDbContext
{
    public MerchantDbContext(DbContextOptions<MerchantDbContext> options) : base(options) { }

    public DbSet<MerchantProfile> Merchants => Set<MerchantProfile>();
    public DbSet<OperatingHours> OperatingHours => Set<OperatingHours>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("merchant");

        builder.AddInboxStateEntity();
        builder.AddOutboxStateEntity();
        builder.AddOutboxMessageEntity();

        builder.Entity<MerchantProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OwnerId).IsUnique();
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Name);

            // Concurrency handled by domain guards + pessimistic locks.

            entity.Property(e => e.Name).HasMaxLength(MerchantConstants.MaxNameLength).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(MerchantConstants.MaxSlugLength).IsRequired();
            entity.Property(e => e.Bio).HasMaxLength(MerchantConstants.MaxBioLength);
            entity.Property(e => e.LogoUrl).HasMaxLength(MerchantConstants.MaxUrlLength);
            entity.Property(e => e.Description).HasMaxLength(MerchantConstants.MaxDescriptionLength);
            entity.Property(e => e.ContactEmail).HasMaxLength(MerchantConstants.MaxEmailLength);
            entity.Property(e => e.ContactPhone).HasMaxLength(MerchantConstants.MaxPhoneLength);
            entity.Property(e => e.Category).HasMaxLength(MerchantConstants.MaxCategoryLength);
            entity.Property(e => e.Website).HasMaxLength(MerchantConstants.MaxUrlLength);
            entity.Property(e => e.Timezone).HasMaxLength(MerchantConstants.MaxTimezoneLength);
            entity.Property(e => e.RejectionReason).HasMaxLength(MerchantConstants.MaxReasonLength);
            entity.Property(e => e.SuspensionReason).HasMaxLength(MerchantConstants.MaxReasonLength);
            entity.Property(e => e.ApprovedBy).HasMaxLength(MerchantConstants.MaxUserLength);
            entity.Property(e => e.RejectedBy).HasMaxLength(MerchantConstants.MaxUserLength);
            entity.Property(e => e.SuspendedBy).HasMaxLength(MerchantConstants.MaxUserLength);
            entity.Property(e => e.DeactivatedBy).HasMaxLength(MerchantConstants.MaxUserLength);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(MerchantConstants.MaxStatusLength);
        });

        builder.Entity<OperatingHours>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MerchantId);
            entity.Property(e => e.IsOpen).HasDefaultValue(true);
        });
    }
}
