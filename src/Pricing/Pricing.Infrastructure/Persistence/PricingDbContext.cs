using Microsoft.EntityFrameworkCore;

namespace Haworks.Pricing.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the pricing-svc Postgres database.
///
/// Scaffolded by 'wave run' as an empty shell. L1 tracks add their entities
/// + DbSets via partial classes (one partial file per track) so this base
/// file is never edited after L0 — keeping the parallel-execution contract.
/// </summary>
public partial class PricingDbContext : DbContext
{
    public PricingDbContext(DbContextOptions<PricingDbContext> options) : base(options) { }
}
