using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Application.Requests.Sagas;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Privacy.Infrastructure.Persistence;

internal sealed class SagaStateRepository : ISagaStateRepository
{
    private readonly PrivacyDbContext _db;

    public SagaStateRepository(PrivacyDbContext db)
    {
        _db = db;
    }

    public Task<PrivacyRequestState?> FindAsync(Guid requestId, CancellationToken cancellationToken = default)
        => _db.Set<PrivacyRequestState>()
              .AsNoTracking()
              .FirstOrDefaultAsync(s => s.CorrelationId == requestId, cancellationToken);
}
