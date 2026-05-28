using FluentAssertions;
using Haworks.Audit.Application.Capture;
using Haworks.Audit.Infrastructure.Persistence;
using Haworks.Contracts.Orders;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Audit.Integration;

[Collection("AuditIntegration")]
public sealed class IdempotencyTests
{
    private readonly AuditWebAppFactory _factory;

    public IdempotencyTests(AuditWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SameMessageId_ShouldBeCapturedOnlyOnce()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Start();

        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act — publish the same event twice with the same MessageId
        await harness.Bus.Publish(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TotalAmountCents = 5000L,
            CustomerEmail = "idempotent@example.com",
            Currency = "USD"
        }, context => context.MessageId = messageId);

        await harness.Bus.Publish(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TotalAmountCents = 5000L,
            CustomerEmail = "idempotent@example.com",
            Currency = "USD"
        }, context => context.MessageId = messageId);

        // Wait for the consumer to process at least one message
        // Poll until consumer has processed at least one OrderCreatedEvent
        for (var i = 0; i < 20; i++)
        {
            if (harness.Consumed.Select<OrderCreatedEvent>().Any()) break;
            await Task.Delay(500);
        }

        // Flush the AuditWriter batch channel so rows are committed to DB.
        // IAuditWriter is registered as singleton; FlushAsync completes the
        // channel and drains the remaining batch.
        var writer = _factory.Services.GetRequiredService<IAuditWriter>();
        await writer.FlushAsync(CancellationToken.None);

        // Assert with a fresh scope to avoid EF tracking cache
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var events = await db.AuditEvents
            .Where(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString())
            .ToListAsync();

        events.Should().HaveCount(1, "the second message with the same MessageId should be ignored by the unique index");

        await harness.Stop();
    }
}
