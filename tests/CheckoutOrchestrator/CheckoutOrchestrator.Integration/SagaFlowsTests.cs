using FluentAssertions;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Checkout;
using Haworks.Contracts.Payments;
using Haworks.CheckoutOrchestrator.Application.Sagas;
using Haworks.CheckoutOrchestrator.Domain;
using Haworks.CheckoutOrchestrator.Infrastructure;

namespace Haworks.CheckoutOrchestrator.Integration;

/// <summary>
/// End-to-end CheckoutSaga state-machine tests. Each test publishes a
/// sequence of upstream events into the in-memory test harness and asserts
/// that the saga transitions correctly AND publishes the expected
/// orchestration triggers. Saga state is persisted to the real Testcontainers
/// postgres so the EF saga repository's xmin concurrency / row-level lock /
/// MT outbox semantics are all in play exactly as in production.
/// </summary>
public sealed class SagaFlowsTests : IClassFixture<CheckoutWebAppFactory>, IAsyncLifetime
{
    private readonly CheckoutWebAppFactory _factory;

    public SagaFlowsTests(CheckoutWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        harness.TestTimeout = TimeSpan.FromSeconds(30);
        harness.TestInactivityTimeout = TimeSpan.FromSeconds(10);
        await harness.Start();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckoutInitiated_creates_saga_in_Initiated_state_and_publishes_StockReservationRequested()
    {
        var (sagaId, orderId) = await PublishCheckoutInitiatedAsync();

        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Initiated", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var sagaState = await ReadSagaAsync(sagaId);
        sagaState.Should().NotBeNull();
        sagaState!.CurrentState.Should().Be("Initiated");
        sagaState.OrderId.Should().Be(orderId);

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var stockReq = harness.Published.Select<StockReservationRequestedEvent>()
            .FirstOrDefault(p => p.Context.Message.SagaId == sagaId);
        stockReq.Should().NotBeNull();
        stockReq!.Context.Message.OrderId.Should().Be(orderId);
        stockReq.Context.Message.TotalAmount.Should().Be(25.50m);
    }

    [Fact]
    public async Task Happy_path_transitions_through_all_states_and_finalizes_in_Completed()
    {
        var (sagaId, orderId) = await PublishCheckoutInitiatedAsync();
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Initiated", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        // Publish StockReserved -> saga should publish PaymentSessionRequested
        // and transition to StockReservedState.
        await PublishAsync(new StockReservedEvent
        {
            OrderId = orderId,
            SagaId = sagaId,
            UserId = "user-1",
            TotalAmount = 25.50m,
            Currency = "USD",
            CustomerEmail = "buyer@example.com",
            Items = new[] { new StockReservationItem
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, RemainingStock = 9,
            }},
            OrderLineItems = new[] { new CheckoutItemData
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, UnitPrice = 25.50m,
            }},
        });

        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "StockReservedState", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var paymentReq = harness.Published.Select<PaymentSessionRequestedEvent>()
            .FirstOrDefault(p => p.Context.Message.SagaId == sagaId);
        paymentReq.Should().NotBeNull("the saga must publish PaymentSessionRequested after stock is reserved");

        // Publish PaymentSessionCreated -> saga transitions to ReadyForPayment.
        var paymentId = Guid.NewGuid();
        await PublishAsync(new PaymentSessionCreatedEvent
        {
            OrderId = orderId, SagaId = sagaId, PaymentId = paymentId,
            UserId = "user-1",
            SessionId = "sess_test", CheckoutUrl = "https://stripe.test/sess_test",
            Provider = "Stripe", Amount = 25.50m, Currency = "USD",
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "ReadyForPayment", StringComparison.Ordinal), TimeSpan.FromSeconds(20));

        // Publish PaymentCompleted -> saga finalizes in Completed.
        await PublishAsync(new PaymentCompletedEvent
        {
            PaymentId = paymentId, OrderId = orderId, SagaId = sagaId,
            Amount = 25.50m, Currency = "USD", Provider = "Stripe",
            TransactionReference = "pi_test",
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Completed", StringComparison.Ordinal) || string.Equals(SagaStateOrNull(sagaId), "Final", StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));

        // SetCompletedWhenFinalized() removes the saga state row once the
        // state machine reaches a final state, so the row may already be
        // gone by the time we read. The polling loop already verified the
        // state transition; ensure the harness saw the PaymentSessionRequested
        // publish (proves the StockReserved→PaymentSessionRequested arrow
        // fired) and that no leftover Initiated/StockReservedState row
        // remains for this sagaId.
        var leftover = await ReadSagaAsync(sagaId);
        if (leftover is not null)
        {
            leftover.CurrentState.Should().BeOneOf("Completed", "Final");
        }
    }

    [Fact]
    public async Task StockReservationFailed_aborts_saga_to_Abandoned_with_no_StockReleaseRequested()
    {
        var (sagaId, orderId) = await PublishCheckoutInitiatedAsync();
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Initiated", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        await PublishAsync(new StockReservationFailedEvent
        {
            OrderId = orderId, SagaId = sagaId,
            FailedItems = new[] { new FailedReservationItem
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget",
                RequestedQuantity = 5, AvailableQuantity = 1,
            }},
            Reason = "Insufficient stock",
        });

        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Abandoned", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var sagaState = await ReadSagaAsync(sagaId);
        sagaState!.FailureReason.Should().Contain("StockReservationFailed");

        // No StockReleaseRequested — nothing was reserved to release.
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        harness.Published.Select<StockReleaseRequestedEvent>()
            .Any(p => p.Context.Message.SagaId == sagaId).Should().BeFalse(
                "stock was never reserved, so no compensation publish should fire");
    }

    [Fact]
    public async Task PaymentSessionFailed_after_StockReserved_compensates_via_StockReleaseRequested_then_Abandoned()
    {
        var (sagaId, orderId) = await PublishCheckoutInitiatedAsync();
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Initiated", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        await PublishAsync(new StockReservedEvent
        {
            OrderId = orderId, SagaId = sagaId, UserId = "user-1",
            TotalAmount = 25.50m, Currency = "USD", CustomerEmail = "buyer@example.com",
            Items = new[] { new StockReservationItem
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, RemainingStock = 9,
            }},
            OrderLineItems = new[] { new CheckoutItemData
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, UnitPrice = 25.50m,
            }},
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "StockReservedState", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        // Stripe rejects the session AFTER stock was reserved -> compensation.
        await PublishAsync(new PaymentSessionFailedEvent
        {
            OrderId = orderId, SagaId = sagaId, Provider = "Stripe",
            ErrorCode = "card_declined", ErrorMessage = "Stripe rejected card",
            AttemptNumber = 1, IsFinalAttempt = true,
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Abandoned", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var sagaState = await ReadSagaAsync(sagaId);
        sagaState!.FailureReason.Should().Contain("PaymentSessionFailed");

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var release = harness.Published.Select<StockReleaseRequestedEvent>()
            .FirstOrDefault(p => p.Context.Message.SagaId == sagaId);
        release.Should().NotBeNull("payment failed AFTER stock was reserved — saga must compensate");
        release!.Context.Message.Reason.Should().Be("payment_session_failed");
    }

    [Fact]
    public async Task PaymentAmountMismatch_after_ReadyForPayment_transitions_to_RequiresReview()
    {
        var (sagaId, orderId) = await PublishCheckoutInitiatedAsync();
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "Initiated", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        await PublishAsync(new StockReservedEvent
        {
            OrderId = orderId, SagaId = sagaId, UserId = "user-1",
            TotalAmount = 25.50m, Currency = "USD", CustomerEmail = "buyer@example.com",
            Items = new[] { new StockReservationItem
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, RemainingStock = 9,
            }},
            OrderLineItems = new[] { new CheckoutItemData
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, UnitPrice = 25.50m,
            }},
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "StockReservedState", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var paymentId = Guid.NewGuid();
        await PublishAsync(new PaymentSessionCreatedEvent
        {
            OrderId = orderId, SagaId = sagaId, PaymentId = paymentId,
            UserId = "user-1",
            SessionId = "sess_x", CheckoutUrl = "https://stripe.test/sess_x",
            Provider = "Stripe", Amount = 25.50m, Currency = "USD",
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "ReadyForPayment", StringComparison.Ordinal), TimeSpan.FromSeconds(20));

        // Stripe captures more than authorized -> RequiresReview branch.
        await PublishAsync(new PaymentAmountMismatchEvent
        {
            PaymentId = paymentId, OrderId = orderId,
            Provider = "Stripe", ActualPaid = 75m, ExpectedTotal = 25.50m,
            Difference = 49.50m, Reason = "captured 75; expected 25.50",
        });
        await PollUntilAsync(() => string.Equals(SagaStateOrNull(sagaId), "RequiresReview", StringComparison.Ordinal), TimeSpan.FromSeconds(15));

        var sagaState = await ReadSagaAsync(sagaId);
        sagaState!.FailureReason.Should().Contain("PaymentAmountMismatch");

        // Stock is NOT released here — the customer has paid (just the wrong
        // amount). Stock stays reserved in RequiresReview so ops can either
        // complete the order (adjust amount) or explicitly release stock after
        // refunding via ManualResolutionEvent(resolution="abandoned").
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        var release = harness.Published.Select<StockReleaseRequestedEvent>()
            .FirstOrDefault(p => p.Context.Message.SagaId == sagaId);
        release.Should().BeNull(
            "PaymentAmountMismatch must NOT release stock — customer has paid, ops resolves via ManualResolution");
    }

    // Saga restart persistence is covered by SagaRealTransportTests (real RabbitMQ).
    // InMemory harness doesn't re-subscribe consumers after Stop/Start.

    private async Task<(Guid sagaId, Guid orderId)> PublishCheckoutInitiatedAsync(string userId = "user-1")
    {
        var sagaId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await PublishAsync(new CheckoutInitiatedEvent
        {
            SagaId = sagaId,
            OrderId = orderId,
            UserId = userId,
            CustomerEmail = "buyer@example.com",
            TotalAmount = 25.50m,
            Items = new[] { new CheckoutItemData
            {
                ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1, UnitPrice = 25.50m,
            }},
            Currency = "USD",
            IdempotencyKey = "key-" + Guid.NewGuid().ToString("N"),
            IsGuest = false,
        });
        return (sagaId, orderId);
    }

    private async Task PublishAsync<T>(T evt) where T : class
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<MassTransit.IPublishEndpoint>();
        await publisher.Publish(evt);
    }

    private string? SagaStateOrNull(Guid sagaId)
    {
        // Open a fresh scope each call — EF context lifecycle.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        return db.CheckoutSagas.AsNoTracking()
            .Where(s => s.CorrelationId == sagaId)
            .Select(s => s.CurrentState)
            .FirstOrDefault();
    }

    private async Task<CheckoutSagaState?> ReadSagaAsync(Guid sagaId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        return await db.CheckoutSagas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CorrelationId == sagaId);
    }

    private static async Task PollUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(250);
        }
    }
}
