using Haworks.BuildingBlocks.Common;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.CheckoutOrchestrator.Application.Queries;

public sealed record ListCheckoutSagasQuery(
    string? State,
    DateTime? From,
    DateTime? To,
    int Limit,
    int Offset,
    string? UserId,
    bool IsAdmin) : IRequest<Result<IReadOnlyList<CheckoutSagaDto>>>;

internal sealed class ListCheckoutSagasQueryHandler(ICheckoutDbContext db)
    : IRequestHandler<ListCheckoutSagasQuery, Result<IReadOnlyList<CheckoutSagaDto>>>
{
    private const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<CheckoutSagaDto>>> Handle(
        ListCheckoutSagasQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, MaxLimit);

        var query = db.CheckoutSagas.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.State))
            query = query.Where(s => s.CurrentState == request.State);

        if (request.From.HasValue)
            query = query.Where(s => s.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(s => s.CreatedAt <= request.To.Value);

        if (!request.IsAdmin && !string.IsNullOrWhiteSpace(request.UserId))
            query = query.Where(s => s.UserId == request.UserId);

        var rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(request.Offset)
            .Take(limit)
            .Select(s => new CheckoutSagaDto(
                s.CorrelationId,
                s.CurrentState,
                s.OrderId,
                s.PaymentId,
                s.PaymentCheckoutUrl,
                s.FailureReason,
                s.CreatedAt))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<CheckoutSagaDto>>(rows);
    }
}
