using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Haworks.Pricing.Infrastructure.Persistence;

public class PricingDbContextFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    public PricingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PricingDbContext>();
        builder.UseNpgsql("Host=localhost;Database=pricing_design");
        return new PricingDbContext(builder.Options);
    }
}
