using System.Security.Cryptography;
using System.Text;
using Haworks.BuildingBlocks.Common;
using Haworks.CheckoutOrchestrator.Application.Telemetry;
using Haworks.CheckoutOrchestrator.Application.Interfaces;
using Haworks.Contracts.Checkout;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.CheckoutOrchestrator.Application.Commands;

public sealed record StartCheckoutCommand(
    Guid SagaId,
    Guid OrderId,
    string UserId,
    string CustomerEmail,
    long TotalAmountCents,
    string IdempotencyKey,
    IReadOnlyList<CheckoutItemData> Items,
    string? Currency = null
) : IRequest<Result<StartCheckoutResponse>>;

public sealed record StartCheckoutResponse(Guid SagaId, Guid OrderId);

internal sealed class StartCheckoutCommandHandler(
    IPublishEndpoint publishEndpoint,
    ICheckoutDbContext db
) : IRequestHandler<StartCheckoutCommand, Result<StartCheckoutResponse>>
{
    public async Task<Result<StartCheckoutResponse>> Handle(StartCheckoutCommand request, CancellationToken ct)
    {
        // Idempotency is handled by MassTransit's InboxState — not by querying
        // saga state tables. Querying sagas here poisons the DbContext change
        // tracker and causes tracking conflicts when the saga consumer tries
        // to Add the new instance (same scoped DbContext).

        // Derive a deterministic SagaId from the IdempotencyKey so that retried
        // HTTP requests with the same key never produce a second saga instance.
        // Always derive from IdempotencyKey, ignore user-provided SagaId to prevent interference.
        if (string.IsNullOrEmpty(request.IdempotencyKey))
        {
            return Result.Failure<StartCheckoutResponse>(
                Error.Validation("StartCheckout.IdempotencyKeyRequired", "IdempotencyKey is required for checkout operations."));
        }

        var hashInput = request.IdempotencyKey;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var sagaId = new Guid(hash.AsSpan(0, 16));
        var orderId = request.OrderId == Guid.Empty ? Guid.NewGuid() : request.OrderId;

        using var activity = CheckoutActivities.Source.StartActivity("checkout.saga.start");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("customer.id", request.UserId);
        activity?.SetTag("checkout.total_amount_cents", request.TotalAmountCents);
        activity?.SetTag("checkout.item_count", request.Items.Count);

        // H6 — Publish writes to the outbox; SaveChanges commits both atomically
        await publishEndpoint.Publish(new CheckoutInitiatedEvent
        {
            SagaId = sagaId,
            OrderId = orderId,
            UserId = request.UserId,
            CustomerEmail = request.CustomerEmail,
            TotalAmountCents = request.TotalAmountCents,
            Items = request.Items,
            IdempotencyKey = request.IdempotencyKey,
            IsGuest = false,
            Currency = request.Currency ?? "USD",
        }, ct);

        await db.SaveChangesAsync(ct);

        return Result.Success(new StartCheckoutResponse(sagaId, orderId));
    }
}
