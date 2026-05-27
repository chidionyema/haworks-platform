using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Haworks.Shipping.Api.Infrastructure;

public class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ShippingDbContext>();
        builder.UseNpgsql("Host=localhost;Database=shipping_design");
        return new ShippingDbContext(builder.Options);
    }
}
