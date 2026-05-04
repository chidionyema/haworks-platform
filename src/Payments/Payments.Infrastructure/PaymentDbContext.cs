using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Haworks.Payments.Domain;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Payments.Infrastructure;

public class PaymentDbContext : DbContext
{
    private readonly IHostEnvironment _environment;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICurrentUserService _currentUserService;

    public PaymentDbContext(
        DbContextOptions<PaymentDbContext> options,
        IHostEnvironment environment,
        ILoggerFactory loggerFactory,
        ICurrentUserService currentUserService)
        : base(options)
    {
        _environment = environment;
        _loggerFactory = loggerFactory;
        _currentUserService = currentUserService;

        ChangeTracker.LazyLoadingEnabled = false;
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseLoggerFactory(_loggerFactory);
        if (_environment.IsDevelopment()) optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.OrderId).IsRequired();
            entity.Property(p => p.UserId).HasMaxLength(450).IsRequired();
            entity.Property(p => p.SagaId).IsRequired();

            entity.Property(p => p.Amount).HasColumnType("numeric(18,2)").IsRequired();
            entity.Property(p => p.Tax).HasColumnType("numeric(18,2)").IsRequired();
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();

            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(p => p.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(p => p.PaymentMethod).HasMaxLength(50);

            entity.Property(p => p.ProviderSessionId).HasMaxLength(500);
            entity.Property(p => p.ProviderTransactionId).HasMaxLength(500);
            entity.Property(p => p.ProviderCheckoutUrl).HasMaxLength(2000);

            entity.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            entity.HasIndex(p => p.OrderId).HasDatabaseName("IX_Payments_OrderId");
            entity.HasIndex(p => p.UserId).HasDatabaseName("IX_Payments_UserId");
            entity.HasIndex(p => p.SagaId).HasDatabaseName("IX_Payments_SagaId");
            entity.HasIndex(p => new { p.Provider, p.ProviderSessionId })
                .HasDatabaseName("IX_Payments_Provider_ProviderSessionId");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("Subscriptions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.UserId).HasMaxLength(450).IsRequired();
            entity.Property(s => s.ProviderSubscriptionId).HasMaxLength(500).IsRequired();
            entity.Property(s => s.PlanId).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
            
            entity.HasIndex(s => s.UserId).HasDatabaseName("IX_Subscriptions_UserId");
            entity.HasIndex(s => s.ProviderSubscriptionId).IsUnique().HasDatabaseName("IX_Subscriptions_ProviderId");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.ToTable("SubscriptionPlans");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.InternalPlanId).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Price).HasColumnType("numeric(18,2)").IsRequired();
            entity.Property(p => p.ProviderPriceIds).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(p => p.InternalPlanId).IsUnique().HasDatabaseName("IX_SubscriptionPlans_PlanId");
        });

        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.ToTable("WebhookEvents");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.ProviderEventId).HasMaxLength(500).IsRequired();
            entity.Property(w => w.EventType).HasMaxLength(200).IsRequired();
            entity.Property(w => w.EventJson).HasColumnType("jsonb").IsRequired();
            entity.Property(w => w.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(w => w.HandlerType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(w => w.Error).HasMaxLength(2000);

            entity.HasIndex(w => new { w.Provider, w.ProviderEventId }).IsUnique().HasDatabaseName("IX_WebhookEvents_Provider_Id");
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void StampAuditFields()
    {
        var entries = ChangeTracker.Entries<AuditableEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);
        foreach (var entry in entries)
        {
            entry.Entity.LastModifiedDate = DateTime.UtcNow;
            entry.Entity.LastModifiedBy = _currentUserService.UserId ?? "system";
            entry.Entity.ModifiedFromIp = _currentUserService.ClientIp ?? "unknown";
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.CreatedBy = _currentUserService.UserId ?? "system";
                entry.Entity.CreatedFromIp = _currentUserService.ClientIp ?? "unknown";
            }
        }
    }
}
