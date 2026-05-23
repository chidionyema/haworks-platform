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

    [Fact(Skip = "Requires partition for current date — tracked for fix")]
    public async Task SameMessageId_ShouldBeCapturedOnlyOnce()
    {
        // Arrange
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

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

        // Poll for up to 10 seconds
        for (int i = 0; i < 20; i++)
        {
            var count = await dbContext.AuditEvents.CountAsync(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString());
            if (count > 0) break;
            await Task.Delay(500);
        }

        // Wait a bit more to see if a second one arrives (it shouldn't)
        await Task.Delay(2000);

        var finalEvents = await dbContext.AuditEvents
            .Where(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString())
            .ToListAsync();

        finalEvents.Should().HaveCount(1, "because the second message with the same MessageId should be ignored by the unique index");

        await harness.Stop();
    }
}
