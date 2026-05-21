using MassTransit;
using Haworks.BuildingBlocks.Messaging;
using Haworks.Payments.Domain;
using Haworks.Payments.Application.Telemetry;
using Haworks.Contracts.Payments;

namespace Haworks.Payments.Application.Sagas;

// CONCURRENCY GUARD NOTE:
// Duplicate concurrent refund sagas for the same refund are prevented by two layers:
//   1. API layer: the IdempotencyKey on the RefundRequest endpoint ensures the HTTP
//      caller cannot issue the same refund twice (unique constraint on IdempotencyJournal).
//   2. Domain layer: Payment.RecordRefund uses a concurrency token (Version) so a
//      second saga instance for the same PaymentId will hit DbUpdateConcurrencyException
//      and be nacked/retried, effectively serialising concurrent attempts.
// No additional unique-index guard is needed in the saga repository itself.
public sealed class RefundSaga : MassTransitStateMachine<RefundSagaState>
{
    public RefundSaga(SagaTransitionAuditObserver<RefundSagaState>? auditObserver = null)
    {
        if (auditObserver != null) ConnectStateObserver(auditObserver);
        InstanceState(s => s.CurrentState);

        Schedule(
            () => RefundTimeoutSchedule,
            instance => instance.RefundTimeoutTokenId,
            s =>
            {
                s.Received = r => r.CorrelateById(ctx => ctx.Message.RefundId);
            });

        Schedule(
            () => ReviewEscalationSchedule,
            instance => instance.ReviewEscalationTokenId,
            s =>
            {
                s.Delay = TimeSpan.FromHours(72);
                s.Received = r => r.CorrelateById(ctx => ctx.Message.RefundId);
            });

        Event(() => RefundRequested, e => e.CorrelateById(ctx => ctx.Message.RefundId));
        Event(() => ProviderRefundInitiated, e => e.CorrelateById(ctx => ctx.Message.RefundId));
        Event(() => ProviderRefundSucceeded, e => e.CorrelateById(ctx => ctx.Message.RefundId));
        Event(() => ProviderRefundFailed, e => e.CorrelateById(ctx => ctx.Message.RefundId));
        Event(() => RefundCancelledByOperator, e => e.CorrelateById(ctx => ctx.Message.RefundId));
        Event(() => RefundApprovedByOperator, e => e.CorrelateById(ctx => ctx.Message.RefundId));

        Initially(
            When(RefundRequested)
                .Then(ctx =>
                {
                    var msg = ctx.Message;
                    var saga = ctx.Saga;
                    saga.OrderId = msg.OrderId;
                    saga.PaymentId = msg.PaymentId;
                    saga.RefundId = msg.RefundId;
                    saga.Amount = msg.Amount;
                    saga.Currency = msg.Currency;
                    saga.Reason = msg.Reason ?? "";
                    saga.Provider = msg.Provider ?? "Stripe";
                    saga.RequestedBy = msg.RequestedBy;
                    saga.CreatedAt = DateTime.UtcNow;
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "Initial", "Requested");
                })
                .PublishAsync(ctx => ctx.Init<ProviderRefundInitiationRequestedEvent>(new ProviderRefundInitiationRequestedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    Provider = ctx.Saga.Provider, // RS-04: use provider from event, not hardcoded
                    PaymentId = ctx.Saga.PaymentId,
                    Amount = ctx.Saga.Amount,
                    Currency = ctx.Saga.Currency
                }))
                .Schedule(RefundTimeoutSchedule, ctx => ctx.Init<RefundTimedOutEvent>(new RefundTimedOutEvent
                {
                    RefundId = ctx.Saga.CorrelationId
                }), _ => TimeSpan.FromHours(24))
                .TransitionTo(Requested));

        During(Requested,
            When(ProviderRefundInitiated)
                .Then(ctx =>
                {
                    ctx.Saga.ProviderRefundId = ctx.Message.ProviderRefundId;
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "Requested", "AwaitingProviderConfirmation");
                })
                .TransitionTo(AwaitingProviderConfirmation),
            When(ProviderRefundFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureCategory = RefundFailureCategory.ProviderRefundFailed;
                    ctx.Saga.FailureDetail = $"{ctx.Message.ErrorCode}: {ctx.Message.ErrorMessage}";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "provider_refund_failed");
                })
                .Unschedule(RefundTimeoutSchedule)
                .PublishAsync(ctx => ctx.Init<RefundFailedEvent>(new RefundFailedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    FailureCategory = "ProviderRefundFailed",
                    FailureDetail = ctx.Saga.FailureDetail ?? "Unknown provider error"
                }))
                .Schedule(ReviewEscalationSchedule, ctx => ctx.Init<RefundReviewEscalatedEvent>(new RefundReviewEscalatedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    HoursInReview = 72
                }))
                .TransitionTo(RequiresReview),
            When(RefundTimeoutSchedule.Received)
                .Unschedule(RefundTimeoutSchedule)
                .Then(ctx =>
                {
                    ctx.Saga.FailureCategory = RefundFailureCategory.RefundTimedOut;
                    ctx.Saga.FailureDetail = "provider_initiation_timeout";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "provider_initiation_timeout");
                })
                .PublishAsync(ctx => ctx.Init<RefundStalledEvent>(new RefundStalledEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    HoursSinceRequest = 24
                }))
                .Schedule(ReviewEscalationSchedule, ctx => ctx.Init<RefundReviewEscalatedEvent>(new RefundReviewEscalatedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    HoursInReview = 72
                }))
                .TransitionTo(RequiresReview));

        During(AwaitingProviderConfirmation,
            When(ProviderRefundSucceeded)
                .Then(ctx =>
                {
                    if (ctx.Message.AmountRefunded != ctx.Saga.Amount)
                    {
                        ctx.Saga.FailureDetail =
                            $"Partial refund: requested={ctx.Saga.Amount}, actual={ctx.Message.AmountRefunded}";
                        ctx.Saga.Amount = ctx.Message.AmountRefunded;
                    }
                    EmitTransitionSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "AwaitingProviderConfirmation", "Refunded");
                })
                .Unschedule(RefundTimeoutSchedule)
                .PublishAsync(ctx => ctx.Init<RefundCompletedEvent>(new RefundCompletedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    PaymentId = ctx.Saga.PaymentId,
                    Amount = ctx.Saga.Amount,
                    Currency = ctx.Saga.Currency
                }))
                .TransitionTo(Refunded)
                .Finalize(),
            When(ProviderRefundFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureCategory = RefundFailureCategory.ProviderRefundFailed;
                    ctx.Saga.FailureDetail = $"{ctx.Message.ErrorCode}: {ctx.Message.ErrorMessage}";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "provider_refund_failed_late");
                })
                .Unschedule(RefundTimeoutSchedule)
                .PublishAsync(ctx => ctx.Init<RefundFailedEvent>(new RefundFailedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    FailureCategory = "ProviderRefundFailed",
                    FailureDetail = ctx.Saga.FailureDetail ?? "Unknown provider confirmation error"
                }))
                .Schedule(ReviewEscalationSchedule, ctx => ctx.Init<RefundReviewEscalatedEvent>(new RefundReviewEscalatedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    HoursInReview = 72
                }))
                .TransitionTo(RequiresReview),
            When(RefundTimeoutSchedule.Received)
                .Unschedule(RefundTimeoutSchedule)
                .Then(ctx =>
                {
                    ctx.Saga.FailureCategory = RefundFailureCategory.RefundTimedOut;
                    ctx.Saga.FailureDetail = "Provider did not confirm refund within 24 hours";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "refund_timeout");
                })
                .PublishAsync(ctx => ctx.Init<RefundStalledEvent>(new RefundStalledEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    HoursSinceRequest = 24
                }))
                .Schedule(ReviewEscalationSchedule, ctx => ctx.Init<RefundReviewEscalatedEvent>(new RefundReviewEscalatedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    HoursInReview = 72
                }))
                .TransitionTo(RequiresReview));

        During(RequiresReview,
            When(RefundApprovedByOperator)
                .IfElse(ctx => ctx.Saga.RetryCount >= 3,
                    exhausted => exhausted
                        .Then(ctx =>
                        {
                            ctx.Saga.FailureCategory = RefundFailureCategory.RetriesExhausted;
                            ctx.Saga.FailureDetail = $"Operator re-approved {ctx.Saga.RetryCount} times — max retries exceeded";
                            EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "refund_retries_exhausted");
                        })
                        .Unschedule(ReviewEscalationSchedule)
                        .PublishAsync(ctx => ctx.Init<RefundCancelledEvent>(new RefundCancelledEvent
                        {
                            RefundId = ctx.Saga.CorrelationId,
                            OrderId = ctx.Saga.OrderId,
                            PaymentId = ctx.Saga.PaymentId,
                            Amount = ctx.Saga.Amount,
                            Reason = "retries_exhausted"
                        }))
                        .TransitionTo(Cancelled)
                        .Finalize(),
                    retry => retry
                        .Then(ctx =>
                        {
                            ctx.Saga.RetryCount++;
                            ctx.Saga.FailureCategory = RefundFailureCategory.None;
                            ctx.Saga.FailureDetail = null;
                        })
                        .Unschedule(ReviewEscalationSchedule)
                        .Unschedule(RefundTimeoutSchedule)
                        .PublishAsync(ctx => ctx.Init<ProviderRefundInitiationRequestedEvent>(new ProviderRefundInitiationRequestedEvent
                        {
                            RefundId = ctx.Saga.CorrelationId,
                            Provider = ctx.Saga.Provider,
                            PaymentId = ctx.Saga.PaymentId,
                            Amount = ctx.Saga.Amount,
                            Currency = ctx.Saga.Currency
                        }))
                        .Schedule(RefundTimeoutSchedule, ctx => ctx.Init<RefundTimedOutEvent>(new RefundTimedOutEvent
                        {
                            RefundId = ctx.Saga.CorrelationId
                        }), _ => TimeSpan.FromHours(24))
                        .TransitionTo(AwaitingProviderConfirmation)),
            // H13: ProviderRefundSucceeded arrives while in RequiresReview (timeout race).
            // The provider actually refunded the money — honor it instead of dropping.
            When(ProviderRefundSucceeded)
                .Unschedule(ReviewEscalationSchedule)
                .Unschedule(RefundTimeoutSchedule)
                .PublishAsync(ctx => ctx.Init<RefundCompletedEvent>(new RefundCompletedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    PaymentId = ctx.Saga.PaymentId,
                    Amount = ctx.Saga.Amount,
                    Currency = ctx.Saga.Currency
                }))
                .TransitionTo(Refunded)
                .Finalize(),
            // H4: 72-hour escalation timeout — auto-cancel if no operator action.
            When(ReviewEscalationSchedule!.Received)
                .Then(ctx =>
                {
                    ctx.Saga.FailureCategory = RefundFailureCategory.ReviewEscalationTimeout;
                    ctx.Saga.FailureDetail = "No operator action within 72 hours";
                    EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "review_escalation_timeout");
                })
                .PublishAsync(ctx => ctx.Init<RefundReviewEscalatedEvent>(new RefundReviewEscalatedEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    HoursInReview = 72
                }))
                .PublishAsync(ctx => ctx.Init<RefundCancelledEvent>(new RefundCancelledEvent
                {
                    RefundId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                    PaymentId = ctx.Saga.PaymentId,
                    Amount = ctx.Saga.Amount,
                    Reason = "review_escalation_timeout"
                }))
                .TransitionTo(Cancelled)
                .Finalize());

        // Idempotency: late-arriving duplicate events on a finalized saga
        // (Refunded / Cancelled) silently no-op rather than throwing.
        DuringAny(
            When(ProviderRefundSucceeded)
                .If(ctx => string.Equals(ctx.Saga.CurrentState, Refunded.Name, StringComparison.Ordinal)
                        || string.Equals(ctx.Saga.CurrentState, Cancelled.Name, StringComparison.Ordinal),
                    x => x),
            When(ProviderRefundFailed)
                .If(ctx => string.Equals(ctx.Saga.CurrentState, RequiresReview.Name, StringComparison.Ordinal)
                        || string.Equals(ctx.Saga.CurrentState, Cancelled.Name, StringComparison.Ordinal)
                        || string.Equals(ctx.Saga.CurrentState, Refunded.Name, StringComparison.Ordinal),
                    x => x),
            When(RefundCancelledByOperator)
                .IfElse(ctx => string.Equals(ctx.Saga.CurrentState, Cancelled.Name, StringComparison.Ordinal),
                    noOp => noOp,
                    active => active
                        .Then(ctx =>
                        {
                            ctx.Saga.FailureCategory = RefundFailureCategory.CancelledByOperator;
                            ctx.Saga.FailureDetail = "Cancelled by operator";
                            EmitCompensateSpan(ctx.Saga.CorrelationId, ctx.Saga.OrderId, "refund_cancelled_by_operator");
                        })
                        .Unschedule(RefundTimeoutSchedule)
                        .If(ctx => string.Equals(ctx.Saga.CurrentState, AwaitingProviderConfirmation.Name, StringComparison.Ordinal),
                            x => x.PublishAsync(ctx => ctx.Init<ProviderRefundCancellationRequestedEvent>(new ProviderRefundCancellationRequestedEvent
                            {
                                RefundId = ctx.Saga.CorrelationId,
                                ProviderRefundId = ctx.Saga.ProviderRefundId ?? ""
                            })))
                        .PublishAsync(ctx => ctx.Init<RefundCancelledEvent>(new RefundCancelledEvent
                        {
                            RefundId = ctx.Saga.CorrelationId,
                            OrderId = ctx.Saga.OrderId,
                            PaymentId = ctx.Saga.PaymentId,
                            Amount = ctx.Saga.Amount,
                            Reason = "Cancelled by operator"
                        }))
                        .TransitionTo(Cancelled)
                        .Finalize()));

        SetCompletedWhenFinalized();
    }

    public State Requested { get; private set; } = null!;
    public State AwaitingProviderConfirmation { get; private set; } = null!;
    public State Refunded { get; private set; } = null!;
    public State RequiresReview { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<RefundRequestedEvent> RefundRequested { get; private set; } = null!;
    public Event<ProviderRefundInitiatedEvent> ProviderRefundInitiated { get; private set; } = null!;
    public Event<ProviderRefundSucceededEvent> ProviderRefundSucceeded { get; private set; } = null!;
    public Event<ProviderRefundFailedEvent> ProviderRefundFailed { get; private set; } = null!;
    public Event<RefundCancelledByOperatorEvent> RefundCancelledByOperator { get; private set; } = null!;
    public Event<RefundApprovedByOperatorEvent> RefundApprovedByOperator { get; private set; } = null!;

    public Schedule<RefundSagaState, RefundTimedOutEvent> RefundTimeoutSchedule { get; private set; } = null!;
    public Schedule<RefundSagaState, RefundReviewEscalatedEvent> ReviewEscalationSchedule { get; private set; } = null!;

    private static void EmitCompensateSpan(Guid sagaId, Guid orderId, string reason)
    {
        using var activity = PaymentsActivities.Source.StartActivity("refund.saga.compensate");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("compensate.reason", reason);
    }

    /// <summary>
    /// Emits a discrete <c>refund.saga.transition</c> span for each
    /// happy-path state advancement. Tags carry the from/to state names
    /// and correlation ids so Tempo can reconstruct the full refund lifecycle.
    /// </summary>
    private static void EmitTransitionSpan(Guid sagaId, Guid orderId, string fromState, string toState)
    {
        using var activity = PaymentsActivities.Source.StartActivity("refund.saga.transition");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("from_state", fromState);
        activity?.SetTag("to_state", toState);
    }
}
