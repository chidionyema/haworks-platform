using FluentAssertions;
using Haworks.Audit.Infrastructure.Persistence;
using Haworks.Contracts.Orders;
using MassTransit;
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

    [Fact(Skip = "AuditWriter COPY batch conflicts with other tests in collection — needs per-test writer isolation")]
    public async Task SameMessageId_ShouldBeCapturedOnlyOnce()
    {
        // Arrange — use IBus directly (harness auto-starts with the host)
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        // Clean slate — previous tests in the collection may have in-flight writes.
        // Wait for the AuditWriter batch to drain, then truncate.
        await Task.Delay(2000);
        try { await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE audit.audit_events CASCADE"); } catch { /* partition may not exist */ }

        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act — publish first, wait for it to be written, then publish duplicate
        await bus.Publish(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TotalAmount = 50.00m,
            CustomerEmail = "idempotent@example.com"
        }, context => context.MessageId = messageId);

        // Wait for the first message to be captured before sending the duplicate
        for (int i = 0; i < 20; i++)
        {
            var count = await dbContext.AuditEvents.CountAsync(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString());
            if (count > 0) break;
            await Task.Delay(500);
        }

        // Now publish the duplicate — unique constraint should reject it
        await bus.Publish(new OrderCreatedEvent
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            TotalAmount = 50.00m,
            CustomerEmail = "idempotent@example.com"
        }, context => context.MessageId = messageId);

        // Wait to see if a second one arrives (it shouldn't)
        await Task.Delay(3000);

        var finalEvents = await dbContext.AuditEvents
            .Where(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString())
            .ToListAsync();

        finalEvents.Should().HaveCount(1, "because the second message with the same MessageId should be ignored by the unique index");
    }
}
