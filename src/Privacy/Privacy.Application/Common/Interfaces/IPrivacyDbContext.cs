using Haworks.Privacy.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Haworks.Privacy.Application.Common.Interfaces;

public interface IPrivacyDbContext
{
    DbSet<PrivacyRequest> PrivacyRequests { get; }
    DbSet<PrivacyRequestStep> PrivacyRequestSteps { get; }
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
