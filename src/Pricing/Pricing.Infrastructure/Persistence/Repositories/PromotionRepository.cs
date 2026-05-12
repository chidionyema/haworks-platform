using Haworks.Pricing.Application.Promotions;
using Haworks.Pricing.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Pricing.Infrastructure.Persistence.Repositories;

public sealed class PromotionRepository : IPromotionRepository
{
    private readonly PricingDbContext _db;

    public PromotionRepository(PricingDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<Promotion>> GetActivePromotionsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Promotions
            .Include(p => p.Rules)
            .Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now)
            .ToListAsync(ct);
    }
}
