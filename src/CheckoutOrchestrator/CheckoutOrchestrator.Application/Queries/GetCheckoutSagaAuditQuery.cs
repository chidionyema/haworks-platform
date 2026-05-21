using Haworks.BuildingBlocks.Common;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.CheckoutOrchestrator.Application.Queries;

public sealed record GetCheckoutSagaAuditQuery(Guid SagaId)
    : IRequest<Result<IReadOnlyList<SagaTransitionAuditDto>>>;

public sealed record SagaTransitionAuditDto(
    long Id,
    string SagaType,
    Guid CorrelationId,
    string FromState,
    string ToState,
    DateTime OccurredAt);

internal sealed class GetCheckoutSagaAuditQueryHandler(ICheckoutDbContext db)
    : IRequestHandler<GetCheckoutSagaAuditQuery, Result<IReadOnlyList<SagaTransitionAuditDto>>>
{
    public async Task<Result<IReadOnlyList<SagaTransitionAuditDto>>> Handle(
        GetCheckoutSagaAuditQuery request, CancellationToken ct)
    {
        var exists = await db.CheckoutSagas.AsNoTracking()
            .AnyAsync(s => s.CorrelationId == request.SagaId, ct);

        if (!exists)
            return Result.Failure<IReadOnlyList<SagaTransitionAuditDto>>(
                Error.NotFound("CheckoutSaga.NotFound", "Saga not found."));

        var rows = await db.SagaTransitionAudit.AsNoTracking()
            .Where(e => e.CorrelationId == request.SagaId)
            .OrderBy(e => e.Id)
            .Select(e => new SagaTransitionAuditDto(
                e.Id,
                e.SagaType,
                e.CorrelationId,
                e.FromState,
                e.ToState,
                e.OccurredAt))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SagaTransitionAuditDto>>(rows);
    }
}
