using Haworks.Scheduler.Application.Common.Interfaces;
using Haworks.Scheduler.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Scheduler.Infrastructure.Persistence;

public sealed class LeaseRepository : ILeaseRepository
{
    private readonly SchedulerDbContext _db;

    public LeaseRepository(SchedulerDbContext db) => _db = db;

    public async Task<List<VaultLease>> GetActiveLeasesNeedingRotationAsync(int batchSize, CancellationToken ct)
    {
        var leases = await _db.VaultLeases
            .Where(l => l.Status == VaultLeaseStatus.Active)
            .OrderBy(l => l.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return leases.Where(l => l.NeedsRotation()).ToList();
    }

    public Task<List<VaultLease>> GetStaleRotatingLeasesAsync(int staleMinutes, CancellationToken ct)
    {
        var staleThreshold = DateTimeOffset.UtcNow.AddMinutes(-staleMinutes);

        return _db.VaultLeases
            .Where(l => l.Status == VaultLeaseStatus.Rotating)
            .Where(l => l.LastRotatedAt == null || l.LastRotatedAt < staleThreshold)
            .ToListAsync(ct);
    }

    public Task<bool> LeaseExistsAsync(string serviceName, string roleName, string credentialType, CancellationToken ct)
    {
        return _db.VaultLeases
            .AnyAsync(l => l.ServiceName == serviceName && l.RoleName == roleName && l.CredentialType == credentialType, ct);
    }

    public Task AddLeaseAsync(VaultLease lease, CancellationToken ct)
    {
        _db.VaultLeases.Add(lease);
        return Task.CompletedTask;
    }

    public Task AddAuditEntryAsync(RotationAuditEntry entry, CancellationToken ct)
    {
        _db.RotationAuditEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
