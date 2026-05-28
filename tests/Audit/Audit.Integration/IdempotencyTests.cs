using FluentAssertions;
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

        // Poll until at least one row lands in the DB via the batched writer.
        // The AuditWriter batches every 200ms; give it up to 15s for consumer
        // processing + batch flush + COPY.
        int count = 0;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            count = await db.AuditEvents
                .CountAsync(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString());
            if (count > 0) break;
        }

        // Wait a bit more to see if a duplicate sneaks in
        await Task.Delay(2000);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var events = await finalDb.AuditEvents
            .Where(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString())
            .ToListAsync();

        events.Should().HaveCount(1, "the second message with the same MessageId should be ignored by the unique index");

        await harness.Stop();
    }
}
