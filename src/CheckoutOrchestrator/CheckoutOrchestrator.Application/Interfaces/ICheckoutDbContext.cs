using Haworks.BuildingBlocks.Messaging;
using Haworks.CheckoutOrchestrator.Application.Sagas;
using Microsoft.EntityFrameworkCore;

namespace Haworks.CheckoutOrchestrator.Application.Interfaces;

public interface ICheckoutDbContext
{
    DbSet<CheckoutSagaState> CheckoutSagas { get; }
    DbSet<SagaTransitionAuditEntry> SagaTransitionAudit { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
