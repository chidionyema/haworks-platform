using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Haworks.BuildingBlocks.Idempotency;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Haworks.Contracts.Orders;
using Haworks.Orders.Application.Telemetry;

namespace Haworks.Orders.Application.Commands;

public sealed record CreateOrderCommand(
    string UserId,
    string CustomerEmail,
    long TotalAmountCents,
    string Currency,
    Guid SagaId,
    string IdempotencyKey,
    IReadOnlyList<CreateOrderLineItem> Items) : IIdempotentCommand, IRequest<Result<Guid>>;

public sealed record CreateOrderLineItem(
    Guid ProductId,
    string ProductName,
    int Quantity,
    long UnitPriceCents);


internal sealed class CreateOrderCommandHandler(
    IOrderRepository orders,
    IPublishEndpoint eventPublisher,
    ILogger<CreateOrderCommandHandler> logger
) : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        using var activity = OrdersActivities.Source.StartActivity("orders.create");
        activity?.SetTag("customer.id", request.UserId);
        activity?.SetTag("order.total_cents", request.TotalAmountCents);
        activity?.SetTag("order.currency", request.Currency);
        activity?.SetTag("order.item_count", request.Items.Count);
        activity?.SetTag("saga.id", request.SagaId);

        // Idempotency: if an order already exists for this SagaId, return its
        // Id rather than creating a duplicate.
        var existing = await orders.GetBySagaIdTrackedAsync(request.SagaId, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "CreateOrderCommand: returning existing order {OrderId} for sagaId {SagaId}",
                existing.Id, request.SagaId);
            return Result.Success(existing.Id);
        }

        var order = Order.Create(
            request.UserId,
            request.TotalAmountCents,
            request.Currency,
            request.SagaId,
            request.IdempotencyKey,
            request.CustomerEmail,
            request.Items.Select(i => (i.ProductId, i.ProductName, i.Quantity, i.UnitPriceCents)));

        await orders.AddAsync(order, ct);

        // M2 fix: Always publish OrderCreatedEvent. If UserId is not a valid GUID,
        // derive a deterministic one from the UserId string so the saga always sees the order.
        var customerGuid = Guid.TryParse(request.UserId, out var parsed)
            ? parsed
            : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(request.UserId)).AsSpan(0, 16));

        if (!Guid.TryParse(request.UserId, out _))
        {
            logger.LogWarning(
                "Order {OrderId}: UserId '{UserId}' is not a Guid — using deterministic hash {DerivedGuid}",
                order.Id, request.UserId, customerGuid);
        }

        await eventPublisher.Publish(new OrderCreatedEvent
        {
            OrderId = order.Id,
            CustomerId = customerGuid,
            TotalAmountCents = order.TotalAmountCents,
            CustomerEmail = order.CustomerEmail,
            Currency = order.Currency,
        }, ct);

        // M1 fix: catch unique constraint violation (23505) on SagaId for concurrent duplicates.
        // On conflict, clear the poisoned change tracker, re-read, and return existing order.
        try
        {
            await orders.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            logger.LogInformation(
                "CreateOrderCommand: concurrent duplicate for sagaId {SagaId}; returning existing order",
                request.SagaId);
            orders.ClearChangeTracker();
            var duplicate = await orders.GetBySagaIdTrackedAsync(request.SagaId, ct);
            if (duplicate is not null)
                return Result.Success(duplicate.Id);
            throw; // constraint was on something else
        }

        logger.LogInformation("Order {OrderId} created for sagaId {SagaId}", order.Id, request.SagaId);
        return Result.Success(order.Id);
    }
}
