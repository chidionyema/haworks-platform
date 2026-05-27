using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Haworks.Media.Api.Infrastructure;

public class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<MediaDbContext>();
        builder.UseNpgsql("Host=localhost;Database=media_design");
        return new MediaDbContext(builder.Options);
    }
}
