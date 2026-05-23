using Haworks.BuildingBlocks.Common;
using Haworks.Payments.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Payments.Application.Queries.Refunds;

public sealed record ListRefundSagasQuery(
    string? State,
    DateTime? From,
    DateTime? To,
    int Limit,
    int Offset) : IRequest<Result<IReadOnlyList<RefundSagaDto>>>;

internal sealed class ListRefundSagasQueryHandler(IPaymentDbContext db)
    : IRequestHandler<ListRefundSagasQuery, Result<IReadOnlyList<RefundSagaDto>>>
{
    private const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<RefundSagaDto>>> Handle(
        ListRefundSagasQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, MaxLimit);

        var query = db.RefundSagas.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.State))
            query = query.Where(s => s.CurrentState == request.State);

        if (request.From.HasValue)
            query = query.Where(s => s.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(s => s.CreatedAt <= request.To.Value);

        var rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(request.Offset)
            .Take(limit)
            .Select(s => new RefundSagaDto(
                s.RefundId,
                s.OrderId,
                s.PaymentId,
                s.CurrentState,
                s.AmountCents,
                s.Currency,
                s.Reason,
                s.Provider,
                s.ProviderRefundId,
                s.FailureDetail,
                s.FailureCategory.ToString(),
                s.CreatedAt))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<RefundSagaDto>>(rows);
    }
}
