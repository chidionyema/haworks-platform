using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Haworks.RulesEngine.Api.Infrastructure;

public class RulesDbContextFactory : IDesignTimeDbContextFactory<RulesDbContext>
{
    public RulesDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<RulesDbContext>();
        builder.UseNpgsql("Host=localhost;Database=rulesengine_design");
        return new RulesDbContext(builder.Options);
    }
}
