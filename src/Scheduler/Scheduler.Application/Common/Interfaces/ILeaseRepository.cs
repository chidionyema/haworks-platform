using Haworks.Scheduler.Domain.Entities;

namespace Haworks.Scheduler.Application.Common.Interfaces;

/// <summary>
/// Repository abstraction for VaultLease and RotationAuditEntry persistence.
/// Implemented by Infrastructure layer using SchedulerDbContext.
/// </summary>
public interface ILeaseRepository
{
    Task<List<VaultLease>> GetActiveLeasesNeedingRotationAsync(int batchSize, CancellationToken ct);
    Task<List<VaultLease>> GetStaleRotatingLeasesAsync(int staleMinutes, CancellationToken ct);
    Task<bool> LeaseExistsAsync(string serviceName, string roleName, string credentialType, CancellationToken ct);
    Task AddLeaseAsync(VaultLease lease, CancellationToken ct);
    Task AddAuditEntryAsync(RotationAuditEntry entry, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
