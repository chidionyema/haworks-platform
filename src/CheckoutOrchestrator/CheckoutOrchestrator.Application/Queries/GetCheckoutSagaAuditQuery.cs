using Haworks.BuildingBlocks.Common;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.CheckoutOrchestrator.Application.Queries;

public sealed record GetCheckoutSagaAuditQuery(Guid SagaId, string? UserId, bool IsAdmin)
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
        var saga = await db.CheckoutSagas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == request.SagaId, ct);

        if (saga is null)
            return Result.Failure<IReadOnlyList<SagaTransitionAuditDto>>(
                Error.NotFound("CheckoutSaga.NotFound", "Saga not found."));

        if (!request.IsAdmin && !string.Equals(saga.UserId, request.UserId, StringComparison.Ordinal))
            return Result.Failure<IReadOnlyList<SagaTransitionAuditDto>>(
                Error.Forbidden("CheckoutSaga.Forbidden", "You are not authorized to view this saga."));

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
