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

    [Fact(Skip = "Flaky: AuditWriter batch channel timing makes this unreliable when run with other tests")]
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

        // Poll for up to 15 seconds with a fresh scope each time to avoid EF caching
        for (int i = 0; i < 30; i++)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var count = await pollDb.AuditEvents.CountAsync(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString());
            if (count > 0) break;
            await Task.Delay(500);
        }

        // Wait a bit more to see if a second one arrives (it shouldn't)
        await Task.Delay(3000);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var finalEvents = await finalDb.AuditEvents
            .Where(e => e.EventType == nameof(OrderCreatedEvent) && e.EntityId == orderId.ToString())
            .ToListAsync();

        finalEvents.Should().HaveCount(1, "the second message with the same MessageId should be ignored by the unique index");

        await harness.Stop();
    }
}
