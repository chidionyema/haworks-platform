using Haworks.BuildingBlocks.Messaging;
using Haworks.Contracts.Privacy;
using Haworks.Privacy.Application.Telemetry;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Haworks.Privacy.Application.Requests.Sagas;

public class PrivacyRequestStateMachine : MassTransitStateMachine<PrivacyRequestState>
{
    private readonly ILogger<PrivacyRequestStateMachine> _logger;

    // All three services must report back before the saga is considered fully covered.
    private static readonly IReadOnlySet<string> AllServices =
        new HashSet<string> { "identity-svc", "orders-svc", "payments-svc" };

    public PrivacyRequestStateMachine(ILogger<PrivacyRequestStateMachine> logger, SagaTransitionAuditObserver<PrivacyRequestState>? auditObserver = null)
    {
        _logger = logger;
        if (auditObserver != null) ConnectStateObserver(auditObserver);
        InstanceState(x => x.CurrentState);

        // PR-02: 7-day timeout for GDPR compliance
        Schedule(() => ErasureTimeoutSchedule, instance => instance.ErasureTimeoutTokenId, s =>
        {
            s.Received = r => r.CorrelateById(ctx => ctx.Message.RequestId);
        });

        Event(() => RequestInitiated, x => x.CorrelateById(m => m.Message.RequestId));
        Event(() => ErasureCompleted, x => x.CorrelateById(m => m.Message.RequestId));
        Event(() => ErasureFailed, x => x.CorrelateById(m => m.Message.RequestId)); // PR-03

        Initially(
            When(RequestInitiated)
                .Then(context =>
                {
                    context.Saga.UserId = context.Message.UserId;
                    context.Saga.RequestType = "Erasure"; // PR-06
                    context.Saga.CreatedAt = DateTime.UtcNow;

                    _logger.LogInformation("Privacy erasure request initiated for user {UserId}, request {RequestId}",
                        context.Message.UserId, context.Message.RequestId);
                })
                .PublishAsync(context => context.Init<PrivacyErasureRequested>(new PrivacyErasureRequested
                {
                    RequestId = context.Message.RequestId,
                    UserId = context.Message.UserId
                }))
                .Schedule(ErasureTimeoutSchedule, ctx => new PrivacyErasureTimedOut { RequestId = ctx.Saga.CorrelationId },
                    _ => TimeSpan.FromDays(7))
                .TransitionTo(Processing)
        );

        During(Processing,
            When(ErasureCompleted)
                .Then(context => RecordCompletion(context.Saga, context.Message.ServiceName))
                .If(context => context.Saga.IdentityCompleted
                            && context.Saga.OrdersCompleted
                            && context.Saga.PaymentsCompleted,
                    binder => binder
                        .Then(ctx =>
                        {
                            ctx.Saga.CompletedAt = DateTime.UtcNow; // PR-05
                            _logger.LogInformation("Privacy erasure completed for user {UserId}, request {RequestId}",
                                ctx.Saga.UserId, ctx.Saga.CorrelationId);
                            EmitSpan(ctx.Saga.CorrelationId, ctx.Saga.UserId, "erasure_completed");
                        })
                        .Unschedule(ErasureTimeoutSchedule)
                        .TransitionTo(Completed)),

            // C2: accumulate failures; only move to Failed once all services have failed
            When(ErasureFailed)
                .Then(context => RecordFailure(context.Saga, context.Message.ServiceName,
                    context.Message.ErrorMessage))
                .If(context => AllServicesAccountedFor(context.Saga),
                    binder => binder
                        .Then(ctx =>
                        {
                            _logger.LogError(
                                "Privacy erasure fully failed for request {RequestId} — all services failed or completed: {FailedServices}",
                                ctx.Saga.CorrelationId, ctx.Saga.FailedServices);
                            EmitSpan(ctx.Saga.CorrelationId, ctx.Saga.UserId, "erasure_failed");
                            PrivacyActivities.ErasureFailed.Add(1);
                        })
                        .Unschedule(ErasureTimeoutSchedule)
                        .PublishAsync(ctx => ctx.Init<PrivacyErasureFailedNotification>(new PrivacyErasureFailedNotification
                        {
                            RequestId = ctx.Message.RequestId,
                            UserId = ctx.Message.UserId,
                            ServiceName = ctx.Message.ServiceName,
                            ErrorMessage = ctx.Message.ErrorMessage
                        }))
                        .TransitionTo(Failed)),

            // PR-02: handle timeout — move to Stalled (watcher will re-drive incomplete services)
            When(ErasureTimeoutSchedule.Received)
                .Then(context =>
                {
                    _logger.LogError("Privacy erasure timed out for request {RequestId}. Identity={Identity}, Orders={Orders}, Payments={Payments}",
                        context.Saga.CorrelationId, context.Saga.IdentityCompleted,
                        context.Saga.OrdersCompleted, context.Saga.PaymentsCompleted);
                    EmitSpan(context.Saga.CorrelationId, context.Saga.UserId, "erasure_stalled");
                })
                .TransitionTo(Stalled)
        );

        // H10: while Stalled, services may still complete — if all finish, transition to Completed
        During(Stalled,
            When(ErasureCompleted)
                .Then(context => RecordCompletion(context.Saga, context.Message.ServiceName))
                .If(context => context.Saga.IdentityCompleted
                            && context.Saga.OrdersCompleted
                            && context.Saga.PaymentsCompleted,
                    binder => binder
                        .Then(ctx =>
                        {
                            ctx.Saga.CompletedAt = DateTime.UtcNow;
                            _logger.LogInformation(
                                "Privacy erasure completed (from Stalled) for user {UserId}, request {RequestId}",
                                ctx.Saga.UserId, ctx.Saga.CorrelationId);
                            EmitSpan(ctx.Saga.CorrelationId, ctx.Saga.UserId, "erasure_completed");
                        })
                        .TransitionTo(Completed)),

            // C2: failures while stalled also accumulate
            When(ErasureFailed)
                .Then(context => RecordFailure(context.Saga, context.Message.ServiceName,
                    context.Message.ErrorMessage))
                .If(context => AllServicesAccountedFor(context.Saga),
                    binder => binder
                        .Then(ctx =>
                        {
                            _logger.LogError(
                                "Privacy erasure fully failed (from Stalled) for request {RequestId}",
                                ctx.Saga.CorrelationId);
                            EmitSpan(ctx.Saga.CorrelationId, ctx.Saga.UserId, "erasure_failed");
                            PrivacyActivities.ErasureFailed.Add(1);
                        })
                        .PublishAsync(ctx => ctx.Init<PrivacyErasureFailedNotification>(new PrivacyErasureFailedNotification
                        {
                            RequestId = ctx.Message.RequestId,
                            UserId = ctx.Message.UserId,
                            ServiceName = ctx.Message.ServiceName,
                            ErrorMessage = ctx.Message.ErrorMessage
                        }))
                        .TransitionTo(Failed))
        );

        // Idempotency: late-arriving or duplicate events on a finalized saga silently no-op.
        DuringAny(
            When(ErasureCompleted)
                .If(ctx => string.Equals(ctx.Saga.CurrentState, nameof(Completed), StringComparison.Ordinal)
                        || string.Equals(ctx.Saga.CurrentState, nameof(Failed), StringComparison.Ordinal),
                    binder => binder.Then(ctx =>
                        _logger.LogInformation(
                            "Ignoring late ErasureCompleted for request {RequestId} in state {State}",
                            ctx.Saga.CorrelationId, ctx.Saga.CurrentState))),
            When(ErasureFailed)
                .If(ctx => string.Equals(ctx.Saga.CurrentState, nameof(Completed), StringComparison.Ordinal)
                        || string.Equals(ctx.Saga.CurrentState, nameof(Failed), StringComparison.Ordinal),
                    binder => binder.Then(ctx =>
                        _logger.LogInformation(
                            "Ignoring late ErasureFailed for request {RequestId} in state {State}",
                            ctx.Saga.CorrelationId, ctx.Saga.CurrentState))));

        // H9: do NOT call SetCompletedWhenFinalized() — the saga row must be retained for the
        //     GDPR audit trail. CompletedAt, IdentityCompletedAt, OrdersCompletedAt, and
        //     PaymentsCompletedAt timestamps serve as the immutable evidence of erasure.
    }

    public State Processing { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;    // PR-03
    public State Stalled { get; private set; } = null!;   // PR-02

    public Event<InitiatePrivacyRequestMessage> RequestInitiated { get; private set; } = null!;
    public Event<PrivacyErasureCompleted> ErasureCompleted { get; private set; } = null!;
    public Event<PrivacyErasureFailed> ErasureFailed { get; private set; } = null!; // PR-03

    public Schedule<PrivacyRequestState, PrivacyErasureTimedOut> ErasureTimeoutSchedule { get; private set; } = null!; // PR-02

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private void RecordCompletion(PrivacyRequestState saga, string serviceName)
    {
        var now = DateTime.UtcNow;
        switch (serviceName)
        {
            case "identity-svc":
                saga.IdentityCompleted = true;
                saga.IdentityCompletedAt ??= now;
                break;
            case "orders-svc":
                saga.OrdersCompleted = true;
                saga.OrdersCompletedAt ??= now;
                break;
            case "payments-svc":
                saga.PaymentsCompleted = true;
                saga.PaymentsCompletedAt ??= now;
                break;
            default:
                _logger.LogWarning("Privacy saga received ErasureCompleted for unknown service: {ServiceName}",
                    serviceName); // PR-07
                break;
        }
    }

    private void RecordFailure(PrivacyRequestState saga, string serviceName, string errorMessage)
    {
        _logger.LogError(
            "Privacy erasure failed for service {ServiceName}, request {RequestId}: {Error}",
            serviceName, saga.CorrelationId, errorMessage);

        var failed = new HashSet<string>(saga.FailedServicesSet) { serviceName };
        saga.FailedServices = string.Join(',', failed);
    }

    /// <summary>
    /// Returns true when every service has either completed or failed, meaning
    /// there is no service still in-flight that could recover the erasure.
    /// </summary>
    private static bool AllServicesAccountedFor(PrivacyRequestState saga)
    {
        var failed = saga.FailedServicesSet;
        return (saga.IdentityCompleted  || failed.Contains("identity-svc"))
            && (saga.OrdersCompleted    || failed.Contains("orders-svc"))
            && (saga.PaymentsCompleted  || failed.Contains("payments-svc"));
    }

    /// <summary>
    /// Emits a discrete <c>privacy.saga.transition</c> span on key state
    /// transitions. The span is start-and-immediately-disposed — it marks
    /// the moment, not a duration. Tags carry ids and the reason so Tempo
    /// can correlate the span across services.
    /// </summary>
    private static void EmitSpan(Guid requestId, Guid userId, string reason)
    {
        using var activity = PrivacyActivities.Source.StartActivity("privacy.saga.transition");
        activity?.SetTag("saga.id", requestId);
        activity?.SetTag("user.id", userId);
        activity?.SetTag("transition.reason", reason);
    }
}
