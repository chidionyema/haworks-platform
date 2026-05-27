using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.Contracts.Checkout;
using Haworks.CheckoutOrchestrator.Domain;
using Haworks.CheckoutOrchestrator.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace Haworks.CheckoutOrchestrator.Integration;

/// <summary>
/// Tests the saga through the REAL RabbitMQ transport — not the in-memory
/// test harness. This catches the exact class of bugs we hit in production:
/// outbox relay, message serialization, exchange bindings, and consumer
/// scope isolation.
/// </summary>
[Collection(CheckoutRealTransportCollection.Name)]
public sealed class SagaRealTransportTests : IAsyncLifetime
{
    private readonly CheckoutRealTransportFactory _factory;
    private HttpClient _client = null!;

    public SagaRealTransportTests(CheckoutRealTransportFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(Skip = "Needs deferred MassTransit bus start — tracked in backlog")]
    public async Task POST_checkouts_creates_saga_instance_via_real_RabbitMQ()
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync("/api/v1/checkouts", new
        {
            sagaId,
            orderId,
            userId = "test-user",
            customerEmail = "test@example.com",
            totalAmount = 39.99m,
            currency = "GBP",
            idempotencyKey = $"test-{sagaId:N}",
            items = new[]
            {
                new { productId = Guid.NewGuid(), productName = "Widget", quantity = 1, unitPriceCents = 3999L, currency = "USD" }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Wait for the saga to be created — message goes through RabbitMQ.
        // 60 attempts × 1s = 60s max to handle CI container startup latency.
        CheckoutSagaState? saga = null;
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(1000);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
            saga = await db.CheckoutSagas.AsNoTracking()
                .FirstOrDefaultAsync(s => s.CorrelationId == sagaId);
            if (saga != null) break;
        }

        saga.Should().NotBeNull("saga should be created within 60 seconds via RabbitMQ");
        saga!.CurrentState.Should().Be("Initiated");
        saga.OrderId.Should().Be(orderId);
        saga.Currency.Should().Be("GBP");
    }
}
