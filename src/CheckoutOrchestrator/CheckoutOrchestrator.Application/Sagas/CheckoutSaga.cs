using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Haworks.BuildingBlocks.Messaging;
using Haworks.CheckoutOrchestrator.Application.Options;
using Haworks.CheckoutOrchestrator.Application.Telemetry;
using Haworks.Contracts.Catalog;
using Haworks.Contracts.Checkout;
using Haworks.Contracts.Payments;

namespace Haworks.CheckoutOrchestrator.Application.Sagas;

/// <summary>
/// CheckoutSaga — orchestrates the cross-service checkout choreography.
///
/// Happy path:
///   Initial   --(CheckoutInitiatedEvent)-->            Initiated
///                  publishes StockReservationRequested
///   Initiated --(StockReservedEvent)-->                StockReserved
///                  publishes PaymentSessionRequested
///                  schedules PaymentExpiry timeout (configurable, default 15 min)
///   StockReserved --(PaymentSessionCreatedEvent)-->    ReadyForPayment
///   ReadyForPayment --(PaymentCompletedEvent)-->       Completed (final)
///                  cancels PaymentExpiry timeout
///
/// Compensation paths:
///   Initiated --(StockReservationFailedEvent)-->       Abandoned (final)
///                  no stock to release
///   StockReserved | ReadyForPayment --(PaymentSessionFailedEvent)-->
///                  publishes StockReleaseRequested
///                  --(immediately)-->                  Abandoned (final)
///   ReadyForPayment --(PaymentExpiry timeout)-->
///                  publishes StockReleaseRequested
///                  --(immediately)-->                  Abandoned (final)
///   ReadyForPayment --(PaymentAmountMismatchEvent)-->  RequiresReview
///                  no stock released yet; ops resolves via ManualResolutionEvent
///
/// Operator escape-hatch:
///   RequiresReview --(ManualResolutionEvent, Resolution="completed")-->  Completed (final)
///   RequiresReview --(ManualResolutionEvent, Resolution="abandoned")-->  Abandoned (final)
///                  publishes StockReleaseRequested + CheckoutSessionExpiredEvent
///
/// Per ADR-0009 the saga owns no business state — only the snapshot
/// needed to drive orchestration. Order/Payment aggregates remain
/// authoritative in their respective services.
/// </summary>
public sealed class CheckoutSaga : MassTransitStateMachine<CheckoutSagaState>
{
    public CheckoutSaga(IOptions<CheckoutOptions> checkoutOptions, ILogger<CheckoutSaga> logger, SagaTransitionAuditObserver<CheckoutSagaState>? auditObserver = null)
    {
        if (auditObserver != null) ConnectStateObserver(auditObserver);
        var options = checkoutOptions.Value;
        InstanceState(s => s.CurrentState);

        // StockReservationTimeout: if the catalog service never responds with
        // StockReservedEvent (or StockReservationFailedEvent) within 5 minutes
        // the saga is stuck in Initiated with stock never reserved. The timeout
        // fires StockReservationTimedOutEvent and the saga transitions to
        // Abandoned. No compensation is needed — stock was never reserved.
        Schedule(
            () => StockReservationTimeoutSchedule,
            instance => instance.StockReservationTimeoutTokenId,
            s =>
            {
                s.Delay = TimeSpan.FromMinutes(5);
                s.Received = r => r.CorrelateById(ctx => ctx.Message.SagaId);
            });

        // PaymentExpiry timeout: stock is reserved on StockReserved transition,
        // payment session lives in StockReserved + ReadyForPayment. If the
        // customer never completes payment within the deadline, the timeout
        // fires PaymentExpired which compensates the same way PaymentSessionFailed
        // does — publish StockReleaseRequested + Abandoned. Without this timer,
        // an abandoned Stripe/PayPal session leaves stock locked indefinitely.
        Schedule(
            () => PaymentExpirySchedule,
            instance => instance.PaymentExpiryTokenId,
            s =>
            {
                s.Delay = TimeSpan.FromMinutes(options.PaymentExpiryMinutes);
                s.Received = r => r.CorrelateById(ctx => ctx.Message.SagaId);
            });

        Event(() => CheckoutInitiated, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => StockReserved, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => StockReservationFailed, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => PaymentSessionCreated, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => PaymentSessionFailed, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => PaymentCompleted, e => e.SelectId(ctx => ctx.Message.SagaId));
        Event(() => PaymentAmountMismatch, e =>
        {
            // Use SagaId for direct correlation when available; fall back to
            // OrderId (index-covered) if SagaId is empty (legacy publishers).
            e.CorrelateBy((state, ctx) =>
                ctx.Message.SagaId != Guid.Empty
                    ? state.CorrelationId == ctx.Message.SagaId
                    : state.OrderId == ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });
        Event(() => ManualResolution, e =>
        {
            e.CorrelateById(ctx => ctx.Message.SagaId);
            e.OnMissingInstance(m => m.Discard());
        });

        Initially(
            When(CheckoutInitiated)
                .Then(ctx =>
                {
                    var msg = ctx.Message;
                    var sagaState = ctx.Saga;

                    if (msg.TotalAmountCents <= 0)
                    {
                        sagaState.FailureReason = "invalid_amount";
                        logger.LogError(
                            "CheckoutSaga rejected: TotalAmountCents={TotalAmountCents} is invalid (OrderId={OrderId}, SagaId={SagaId})",
                            msg.TotalAmountCents, msg.OrderId, sagaState.CorrelationId);
                        return;
                    }

                    sagaState.OrderId = msg.OrderId;
                    sagaState.UserId = msg.UserId;
                    sagaState.CustomerEmail = msg.CustomerEmail;
                    sagaState.TotalAmountCents = msg.TotalAmountCents;
                    sagaState.Currency = msg.Currency ?? throw new InvalidOperationException("Currency is required on CheckoutStartedEvent");
                    sagaState.IdempotencyKey = msg.IdempotencyKey;
                    sagaState.LineItemsJson = JsonSerializer.Serialize(msg.Items);
                    sagaState.CreatedAt = DateTime.UtcNow;
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "Initial", "Initiated");
                })
                .PublishAsync(ctx => ctx.Init<StockReservationRequestedEvent>(new StockReservationRequestedEvent
                {
                    OrderId = ctx.Message.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    UserId = ctx.Message.UserId,
                    CustomerEmail = ctx.Message.CustomerEmail,
                    TotalAmountCents = ctx.Message.TotalAmountCents,
                    Currency = ctx.Saga.Currency,
                    Items = ctx.Message.Items,
                    IdempotencyKey = ctx.Message.IdempotencyKey,
                }))
                .TransitionTo(Initiated));


        During(Initiated,
            When(StockReservationTimeoutSchedule!.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "stock_reservation_timeout";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "stock_reservation_timeout");
                })
                .TransitionTo(Abandoned),
            When(StockReserved)
                .Then(ctx =>
                {
                    ctx.Saga.ReservedItemsJson = JsonSerializer.Serialize(ctx.Message.Items);
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "Initiated", "StockReserved");
                })
                .Unschedule(StockReservationTimeoutSchedule)
                .PublishAsync(ctx => ctx.Init<PaymentSessionRequestedEvent>(new PaymentSessionRequestedEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    AmountCents = ctx.Saga.TotalAmountCents,
                    Currency = ctx.Saga.Currency,
                    UserId = ctx.Saga.UserId,
                    CustomerEmail = ctx.Saga.CustomerEmail,
                    LineItems = ctx.Message.OrderLineItems
                        .Select(li => new PaymentLineItemData
                        {
                            Name = li.ProductName,
                            UnitAmountCents = li.UnitPriceCents,
                            Quantity = li.Quantity,
                        }).ToList(),
                    SuccessUrl = options.SuccessUrl,
                    CancelUrl = options.CancelUrl,
                    IdempotencyKey = ctx.Saga.IdempotencyKey,
                }))
                // Start the 15-min payment-expiry clock the moment stock is
                // reserved. The token is stored on the saga so it can be
                // cancelled when payment lands in time.
                .Schedule(
                    PaymentExpirySchedule,
                    ctx => ctx.Init<PaymentExpiredEvent>(new PaymentExpiredEvent
                    {
                        SagaId = ctx.Saga.CorrelationId,
                        OrderId = ctx.Saga.OrderId,
                    }))
                .TransitionTo(StockReservedState),
            When(StockReservationFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"StockReservationFailed: {ctx.Message.Reason}";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "stock_reservation_failed");
                })
                .Unschedule(StockReservationTimeoutSchedule)
                .TransitionTo(Abandoned));

        During(StockReservedState,
            When(PaymentSessionCreated)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentId = ctx.Message.PaymentId;
                    ctx.Saga.PaymentSessionId = ctx.Message.SessionId;
                    ctx.Saga.PaymentCheckoutUrl = ctx.Message.CheckoutUrl;
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "StockReserved", "ReadyForPayment");
                })
                .TransitionTo(ReadyForPayment),
            When(PaymentSessionFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"PaymentSessionFailed: {ctx.Message.ErrorCode}";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "payment_session_failed");
                })
                .Unschedule(PaymentExpirySchedule)
                .PublishAsync(ctx => ctx.Init<StockReleaseRequestedEvent>(new StockReleaseRequestedEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    Items = DeserializeItems(ctx.Saga.ReservedItemsJson),
                    Reason = "payment_session_failed",
                }))
                .TransitionTo(Abandoned),
            When(PaymentExpirySchedule!.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"PaymentExpired (no payment session created within {options.PaymentExpiryMinutes}min)";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "payment_expired");
                })
                .PublishAsync(ctx => ctx.Init<StockReleaseRequestedEvent>(new StockReleaseRequestedEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    Items = DeserializeItems(ctx.Saga.ReservedItemsJson),
                    Reason = "payment_expired",
                }))
                .PublishAsync(ctx => ctx.Init<CheckoutSessionExpiredEvent>(new CheckoutSessionExpiredEvent
                {
                    SessionId = ctx.Saga.PaymentSessionId ?? string.Empty,
                    PaymentId = ctx.Saga.PaymentId ?? Guid.Empty,
                    OrderId = ctx.Saga.OrderId,
                    Provider = "unknown",
                }))
                .TransitionTo(Abandoned));

        During(ReadyForPayment,
            When(PaymentCompleted)
                .Then(ctx =>
                {
                    // PaymentId already set in StockReserved transition;
                    // the PaymentCompletedEvent carries the same id.
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "ReadyForPayment", "Completed");
                })
                // Customer paid in time — cancel the expiry clock so no
                // orphaned StockReleaseRequested fires later.
                .Unschedule(PaymentExpirySchedule)
                .TransitionTo(Completed)
                .Finalize(),
            When(PaymentSessionFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"PaymentSessionFailed (mid-flight): {ctx.Message.ErrorCode}";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "payment_session_failed_post_session");
                })
                .Unschedule(PaymentExpirySchedule)
                .PublishAsync(ctx => ctx.Init<StockReleaseRequestedEvent>(new StockReleaseRequestedEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    Items = DeserializeItems(ctx.Saga.ReservedItemsJson),
                    Reason = "payment_session_failed_post_session",
                }))
                .TransitionTo(Abandoned),
            When(PaymentExpirySchedule!.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"PaymentExpired (customer abandoned payment session after {options.PaymentExpiryMinutes}min)";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "payment_expired");
                })
                .PublishAsync(ctx => ctx.Init<StockReleaseRequestedEvent>(new StockReleaseRequestedEvent
                {
                    OrderId = ctx.Saga.OrderId,
                    SagaId = ctx.Saga.CorrelationId,
                    Items = DeserializeItems(ctx.Saga.ReservedItemsJson),
                    Reason = "payment_expired",
                }))
                .PublishAsync(ctx => ctx.Init<CheckoutSessionExpiredEvent>(new CheckoutSessionExpiredEvent
                {
                    SessionId = ctx.Saga.PaymentSessionId ?? string.Empty,
                    PaymentId = ctx.Saga.PaymentId ?? Guid.Empty,
                    OrderId = ctx.Saga.OrderId,
                    Provider = "stripe",
                }))
                .TransitionTo(Abandoned),
            When(PaymentAmountMismatch)
                .Then(ctx => ctx.Saga.FailureReason =
                    $"PaymentAmountMismatch: expected={ctx.Message.ExpectedTotalCents / 100m:F2}, actual={ctx.Message.ActualPaidCents / 100m:F2}")
                .Unschedule(PaymentExpirySchedule)
                // Do NOT release stock here — customer has paid. Stock remains
                // reserved in RequiresReview so ops can either complete the order
                // (adjust amount) or explicitly release stock after refunding.
                .TransitionTo(RequiresReview));

        // Operator escape-hatch: RequiresReview is a holding state for
        // amount-mismatch anomalies. An operator publishes ManualResolutionEvent
        // to move the saga forward. Resolution="completed" marks it done;
        // Resolution="abandoned" releases stock and moves to Abandoned.
        During(RequiresReview,
            When(ManualResolution)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"ManualResolution by {ctx.Message.OperatorId}: {ctx.Message.Resolution}";
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "RequiresReview", ctx.Message.Resolution);
                })
                .If(ctx => string.Equals(ctx.Message.Resolution, "completed", StringComparison.OrdinalIgnoreCase),
                    binder => binder
                        .TransitionTo(Completed)
                        .Finalize())
                .If(ctx => string.Equals(ctx.Message.Resolution, "abandoned", StringComparison.OrdinalIgnoreCase),
                    binder => binder
                        .PublishAsync(ctx => ctx.Init<StockReleaseRequestedEvent>(new StockReleaseRequestedEvent
                        {
                            OrderId = ctx.Saga.OrderId,
                            SagaId = ctx.Saga.CorrelationId,
                            Items = DeserializeItems(ctx.Saga.ReservedItemsJson),
                            Reason = "manual_resolution_abandoned",
                        }))
                        .TransitionTo(Abandoned))
                // M5: Log warning when Resolution is neither "completed" nor "abandoned"
                .If(ctx => !string.Equals(ctx.Message.Resolution, "completed", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(ctx.Message.Resolution, "abandoned", StringComparison.OrdinalIgnoreCase),
                    binder => binder
                        .Then(ctx => logger.LogWarning(
                            "ManualResolution for saga {SagaId} has unrecognized Resolution '{Resolution}' from operator {OperatorId} — no state change applied",
                            ctx.Saga.CorrelationId, ctx.Message.Resolution, ctx.Message.OperatorId))));

        // Idempotency: late-arriving events on non-primary states silently
        // no-op rather than throwing UnhandledEventException. Each event
        // guards against every state where it is NOT explicitly handled.
        //
        // Primary handlers:
        //   StockReserved             → During(Initiated)
        //   StockReservationFailed    → During(Initiated)
        //   PaymentSessionCreated     → During(StockReservedState)
        //   PaymentSessionFailed      → During(StockReservedState, ReadyForPayment)
        //   PaymentCompleted          → During(ReadyForPayment)
        //   PaymentAmountMismatch     → During(ReadyForPayment)
        //   ManualResolution          → During(RequiresReview)
        DuringAny(
            When(StockReserved)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, Initiated.Name, StringComparison.Ordinal), ctx => ctx),
            When(StockReservationFailed)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, Initiated.Name, StringComparison.Ordinal), ctx => ctx),
            When(PaymentSessionCreated)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, StockReservedState.Name, StringComparison.Ordinal), ctx => ctx),
            When(PaymentSessionFailed)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, StockReservedState.Name, StringComparison.Ordinal)
                        && !string.Equals(ctx.Saga.CurrentState, ReadyForPayment.Name, StringComparison.Ordinal), ctx => ctx),
            When(PaymentCompleted)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, ReadyForPayment.Name, StringComparison.Ordinal), ctx => ctx),
            When(PaymentAmountMismatch)
                .If(ctx => !string.Equals(ctx.Saga.CurrentState, ReadyForPayment.Name, StringComparison.Ordinal), ctx => ctx));

        // Abandoned and Completed are terminal states. SetCompletedWhenFinalized
        // marks the saga as completed when it reaches Final, but does NOT
        // auto-delete the row — the row remains queryable for auditing.
        // Production cleanup is via a scheduled sweeper, not inline Finalize().
        SetCompletedWhenFinalized();
    }

    // States. MT convention: a property per state, plus reuse of MT's
    // Initial / Final built-ins. "StockReservedState" is renamed to avoid
    // clashing with the StockReserved Event property of the same name.
    public State Initiated { get; private set; } = null!;
    public State StockReservedState { get; private set; } = null!;
    public State ReadyForPayment { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Abandoned { get; private set; } = null!;
    public State RequiresReview { get; private set; } = null!;

    // Inbound events.
    public Event<CheckoutInitiatedEvent> CheckoutInitiated { get; private set; } = null!;
    public Event<StockReservedEvent> StockReserved { get; private set; } = null!;
    public Event<StockReservationFailedEvent> StockReservationFailed { get; private set; } = null!;
    public Event<PaymentSessionCreatedEvent> PaymentSessionCreated { get; private set; } = null!;
    public Event<PaymentSessionFailedEvent> PaymentSessionFailed { get; private set; } = null!;
    public Event<PaymentCompletedEvent> PaymentCompleted { get; private set; } = null!;
    public Event<PaymentAmountMismatchEvent> PaymentAmountMismatch { get; private set; } = null!;
    public Event<ManualResolutionEvent> ManualResolution { get; private set; } = null!;

    // Scheduled ticks. Schedule.Received is the Event the saga reacts to
    // when the timer fires.

    // StockReservationTimeoutSchedule: started on Initially→Initiated transition,
    // cancelled when StockReservedEvent or StockReservationFailedEvent arrives.
    public Schedule<CheckoutSagaState, StockReservationTimedOutEvent> StockReservationTimeoutSchedule { get; private set; } = null!;

    // PaymentExpirySchedule: started on StockReserved→StockReservedState transition,
    // cancelled via .Unschedule(...) on any terminal transition (PaymentCompleted,
    // PaymentSessionFailed, PaymentAmountMismatch).
    public Schedule<CheckoutSagaState, PaymentExpiredEvent> PaymentExpirySchedule { get; private set; } = null!;

    /// <summary>
    /// Emits a discrete <c>checkout.saga.compensate</c> span on each
    /// compensation entry. The span is start-and-immediately-disposed —
    /// it represents the moment the saga decided to compensate, not a
    /// duration. Tags carry the failure reason and ids so Tempo can
    /// correlate the span back to the order/saga across services.
    /// </summary>
    private static void EmitCompensateSpan(Guid sagaId, Guid orderId, string reason)
    {
        using var activity = CheckoutActivities.Source.StartActivity("checkout.saga.compensate");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("compensate.reason", reason);
    }

    /// <summary>
    /// Emits a discrete <c>checkout.saga.transition</c> span for each
    /// happy-path state advancement. Tags carry the from/to state names
    /// and correlation ids so Tempo can reconstruct the full saga lifecycle.
    /// </summary>
    private static void EmitTransitionSpan(Guid sagaId, Guid orderId, string fromState, string toState)
    {
        using var activity = CheckoutActivities.Source.StartActivity("checkout.saga.transition");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("from_state", fromState);
        activity?.SetTag("to_state", toState);
    }

    private static IReadOnlyList<StockReservationItem> DeserializeItems(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<StockReservationItem>();

        // Do NOT swallow the exception: returning an empty list on corrupt JSON
        // would silently release zero stock, leaving reserved inventory locked
        // indefinitely. Throwing surfaces the corruption so the DLQ + ops can
        // investigate and replay after the data is fixed.
        return JsonSerializer.Deserialize<List<StockReservationItem>>(json)
            ?? throw new InvalidOperationException(
                "ReservedItemsJson deserialized to null — saga state is corrupt.");
    }
}
