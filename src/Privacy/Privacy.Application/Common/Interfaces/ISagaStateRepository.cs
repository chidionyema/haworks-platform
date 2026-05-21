using Haworks.Privacy.Application.Requests.Sagas;

namespace Haworks.Privacy.Application.Common.Interfaces;

/// <summary>
/// Read-only access to persisted saga state for query handlers.
/// Decouples the Application layer from EF Core / PrivacyDbContext.
/// </summary>
public interface ISagaStateRepository
{
    Task<PrivacyRequestState?> FindAsync(Guid requestId, CancellationToken cancellationToken = default);
}
