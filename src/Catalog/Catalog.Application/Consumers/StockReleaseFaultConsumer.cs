using MassTransit;
using Haworks.Contracts.Catalog;

namespace Haworks.Catalog.Application.Consumers;

/// <summary>
/// Final-defense observer for stock-release failures. When the regular
/// <see cref="StockReleaseRequestedConsumer"/> exhausts immediate retries
/// AND delayed redeliveries (configured in
/// <c>StockReleaseRequestedConsumerDefinition</c>) MassTransit publishes a
/// <see cref="Fault{StockReleaseRequestedEvent}"/> to the bus. This consumer
/// catches that fault and logs at CRITICAL with the full context an
/// operator needs to unstick the reservation by hand:
///
///   * orderId / sagaId — to look up the order in the Orders DB
///   * items — the productId + quantity tuples that need releasing
///   * exception type + message — root-cause hint
///
/// Critical-level logs are exported to OpenTelemetry and route to the
/// alert channel configured in observability infra. No DB writes here —
/// this consumer's only job is to make the failure loud enough that it
/// can't sit in a poison queue silently.
/// </summary>
public sealed class StockReleaseFaultConsumer(
    ILogger<StockReleaseFaultConsumer> logger
) : IConsumer<Fault<StockReleaseRequestedEvent>>
{
    public Task Consume(ConsumeContext<Fault<StockReleaseRequestedEvent>> context)
    {
        var fault = context.Message;
        var original = fault.Message;
        var firstException = fault.Exceptions.FirstOrDefault();

        logger.LogCritical(
            "Stock release exhausted all retries — MANUAL INTERVENTION REQUIRED. " +
            "OrderId={OrderId} SagaId={SagaId} Items={Items} Reason={Reason} " +
            "Exception={ExceptionType} Message={ExceptionMessage}",
            original.OrderId,
            original.SagaId,
            string.Join(",", original.Items.Select(i => $"{i.ProductId}:{i.Quantity}")),
            original.Reason,
            firstException?.ExceptionType ?? "(unknown)",
            firstException?.Message ?? "(no exception message)");

        // Returning normally ACKs the fault message so it doesn't loop.
        // The original StockReleaseRequestedEvent is already in the broker's
        // _error queue — operators can replay from there after fixing
        // the underlying cause.
        return Task.CompletedTask;
    }
}
