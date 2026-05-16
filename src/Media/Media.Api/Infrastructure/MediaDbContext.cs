namespace Haworks.Media.Api.Infrastructure;

public class MediaDbContext : DbContext
{
    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) { }

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("media");

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Hash).IsRequired().HasMaxLength(64);
            // Unique index scoped per owner — different users may upload the same file
            entity.HasIndex(e => new { e.Hash, e.OwnerId }).IsUnique();
            entity.Property(e => e.MimeType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.UploadKind).HasConversion<string>().HasDefaultValue(UploadKind.SinglePart);
            entity.Property(e => e.S3UploadId).HasMaxLength(256);
            entity.Property(e => e.PartCount).HasDefaultValue(0);
            entity.Property(e => e.OwnerId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => e.OwnerId);

            entity.Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }
}
