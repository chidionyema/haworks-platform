using Haworks.BuildingBlocks.Telemetry;
using Haworks.Contracts.Payments;
using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain;
using Haworks.Payments.Domain.Interfaces;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Haworks.Payments.Infrastructure.Stripe;

/// <summary>
/// Validates Stripe webhook signatures and processes events.
/// Dispatches to the internal session processor upon valid capture.
/// </summary>
internal sealed class StripeWebhookProcessor : IWebhookProcessor
{
    private readonly IPaymentSessionProcessor _paymentProcessor;
    private readonly ISubscriptionManager _subscriptionManager;
    private readonly IWebhookIdempotencyGuard _idempotencyGuard;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ILogger<StripeWebhookProcessor> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly StripeOptions _stripeOptions;

    public PaymentProvider Provider => PaymentProvider.Stripe;

    public StripeWebhookProcessor(
        IPaymentSessionProcessor paymentProcessor,
        ISubscriptionManager subscriptionManager,
        IWebhookIdempotencyGuard idempotencyGuard,
        IPaymentRepository paymentRepository,
        IDomainEventPublisher eventPublisher,
        IOptions<PaymentProviderOptions> providerOptions,
        ILogger<StripeWebhookProcessor> logger,
        ITelemetryService telemetry)
    {
        _paymentProcessor = paymentProcessor ?? throw new ArgumentNullException(nameof(paymentProcessor));
        _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
        _idempotencyGuard = idempotencyGuard ?? throw new ArgumentNullException(nameof(idempotencyGuard));
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _stripeOptions = providerOptions?.Value?.Stripe ?? throw new ArgumentNullException(nameof(providerOptions));
    }

    /// <inheritdoc />
    public async Task<WebhookValidationResult> ValidateAndParseAsync(
        string payload,
        string signature,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return WebhookValidationResult.Failure("Empty payload");
        }

        try
        {
            // Production security: signature is required.
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Rejecting Stripe webhook: missing signature header.");
                return WebhookValidationResult.Failure("Missing signature");
            }

            // Stripe SDK handles tolerance and replay attack protection internally.
            var stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _stripeOptions.WebhookSecret,
                throwOnApiVersionMismatch: false);

            _logger.LogDebug(
                "Validated Stripe webhook event {EventId} of type {EventType}",
                stripeEvent.Id,
                stripeEvent.Type);

            var webhookEvent = new PaymentWebhookEvent
            {
                EventId = stripeEvent.Id,
                EventType = stripeEvent.Type,
                Provider = Provider,
                CreatedAt = stripeEvent.Created,
                Data = stripeEvent.Data.Object,
                RawPayload = payload
            };

            return WebhookValidationResult.Success(webhookEvent);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature validation failed");
            return WebhookValidationResult.Failure($"Invalid signature: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Stripe webhook payload");
            return WebhookValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<WebhookProcessingResult> ProcessEventAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["EventId"] = webhookEvent.EventId,
            ["EventType"] = webhookEvent.EventType,
            ["Provider"] = Provider.ToString()
        });

        // 1. Check idempotency across all events
        if (await _idempotencyGuard.IsAlreadyProcessedAsync(Provider, webhookEvent.EventId, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Stripe event {EventId} already processed, skipping",
                webhookEvent.EventId);
            return WebhookProcessingResult.Skipped("Already processed");
        }

        _logger.LogInformation(
            "Processing webhook: provider={Provider}, eventType={Type}, providerEventId={Id}",
            Provider, webhookEvent.EventType, webhookEvent.EventId);

        try
        {
            var result = webhookEvent.EventType switch
            {
                StripeConstants.EventTypes.CheckoutSessionCompleted => await HandleCheckoutSessionCompletedAsync(webhookEvent, ct),
                StripeConstants.EventTypes.CheckoutSessionExpired => await HandleCheckoutSessionExpiredAsync(webhookEvent, ct),
                StripeConstants.EventTypes.CustomerSubscriptionCreated => await HandleSubscriptionEventAsync(webhookEvent, SubscriptionEventType.Created, ct),
                StripeConstants.EventTypes.CustomerSubscriptionUpdated => await HandleSubscriptionEventAsync(webhookEvent, SubscriptionEventType.Updated, ct),
                StripeConstants.EventTypes.CustomerSubscriptionDeleted => await HandleSubscriptionEventAsync(webhookEvent, SubscriptionEventType.Canceled, ct),
                StripeConstants.EventTypes.InvoicePaymentFailed => await HandleInvoicePaymentFailedAsync(webhookEvent, ct),
                StripeConstants.EventTypes.ChargeRefunded => await HandleChargeRefundedAsync(webhookEvent, ct),
                _ => HandleUnknownEvent(webhookEvent)
            };

            // 2. Persist processing state for idempotency if successful
            if (result.Processed)
            {
                await _idempotencyGuard.MarkProcessedAsync(
                    Provider,
                    webhookEvent.EventId,
                    webhookEvent.EventType,
                    ct).ConfigureAwait(false);
            }

            TrackWebhookEvent(webhookEvent, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing Stripe event {EventId} of type {EventType}",
                webhookEvent.EventId,
                webhookEvent.EventType);

            _telemetry.TrackException(ex);
            throw;
        }
    }

    private async Task<WebhookProcessingResult> HandleCheckoutSessionCompletedAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken ct)
    {
        if (webhookEvent.Data is not Session session)
        {
            return WebhookProcessingResult.Failed("Session data is null");
        }

        _logger.LogInformation(
            "Processing checkout.session.completed for session {SessionId}, mode: {Mode}",
            session.Id,
            session.Mode);

        switch (session.Mode)
        {
            case StripeConstants.SessionModes.Payment:
                return await HandlePaymentSessionAsync(session, ct);

            case StripeConstants.SessionModes.Subscription:
                return await HandleSubscriptionSessionAsync(session, ct);

            default:
                _logger.LogWarning("Unhandled session mode: {Mode}", session.Mode);
                return WebhookProcessingResult.Skipped($"Unhandled session mode: {session.Mode}");
        }
    }

    private async Task<WebhookProcessingResult> HandlePaymentSessionAsync(
        Session session,
        CancellationToken ct)
    {
        var sessionEvent = new PaymentSessionEvent
        {
            SessionId = session.Id,
            TransactionId = session.PaymentIntentId ?? string.Empty,
            Mode = SessionMode.Payment,
            AmountTotal = session.AmountTotal ?? 0,
            Currency = session.Currency ?? "USD",
            Provider = Provider,
            Metadata = session.Metadata != null
                ? new Dictionary<string, string>(session.Metadata)
                : new Dictionary<string, string>()
        };

        await _paymentProcessor.HandleCompletedSessionAsync(sessionEvent, ct).ConfigureAwait(false);

        return WebhookProcessingResult.Success(
            StripeConstants.EventTypes.CheckoutSessionCompleted,
            $"Payment session {session.Id} processed");
    }

    private async Task<WebhookProcessingResult> HandleSubscriptionSessionAsync(
        Session session,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.SubscriptionId))
        {
            _logger.LogWarning(
                "Subscription session {SessionId} missing SubscriptionId",
                session.Id);
            return WebhookProcessingResult.Failed("Missing SubscriptionId");
        }

        // Extract user ID from metadata
        string? userId = null;
        session.Metadata?.TryGetValue("user_id", out userId);

        var subscriptionEvent = new SubscriptionEvent
        {
            SubscriptionId = session.SubscriptionId,
            EventType = SubscriptionEventType.Created,
            NewStatus = SubscriptionStatus.Active,
            UserId = userId ?? string.Empty,
            Provider = Provider,
            Metadata = session.Metadata != null
                ? new Dictionary<string, string>(session.Metadata)
                : new Dictionary<string, string>()
        };

        await _subscriptionManager.HandleSubscriptionEventAsync(subscriptionEvent, ct).ConfigureAwait(false);

        return WebhookProcessingResult.Success(
            StripeConstants.EventTypes.CheckoutSessionCompleted,
            $"Subscription session {session.Id} processed");
    }

    private async Task<WebhookProcessingResult> HandleSubscriptionEventAsync(
        PaymentWebhookEvent webhookEvent,
        SubscriptionEventType eventType,
        CancellationToken ct)
    {
        if (webhookEvent.Data is not global::Stripe.Subscription stripeSubscription)
        {
            _logger.LogWarning(
                "Subscription data is null for event {EventId}",
                webhookEvent.EventId);
            return WebhookProcessingResult.Failed("Subscription data is null");
        }

        var subscriptionEvent = new SubscriptionEvent
        {
            SubscriptionId = stripeSubscription.Id,
            EventType = eventType,
            NewStatus = StripeSubscriptionStatusMapper.FromStripeStatus(stripeSubscription.Status),
            CurrentPeriodEnd = stripeSubscription.CurrentPeriodEnd,
            PlanId = stripeSubscription.Items?.Data?.FirstOrDefault()?.Price?.Id,
            Provider = Provider,
            Metadata = stripeSubscription.Metadata != null
                ? new Dictionary<string, string>(stripeSubscription.Metadata)
                : new Dictionary<string, string>()
        };

        await _subscriptionManager.HandleSubscriptionEventAsync(subscriptionEvent, ct).ConfigureAwait(false);

        return WebhookProcessingResult.Success(
            webhookEvent.EventType,
            $"Subscription {stripeSubscription.Id} event processed");
    }

    private async Task<WebhookProcessingResult> HandleInvoicePaymentFailedAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken ct)
    {
        if (webhookEvent.Data is not Invoice invoice)
        {
            return WebhookProcessingResult.Failed("Invoice data is null");
        }

        if (string.IsNullOrEmpty(invoice.SubscriptionId))
        {
            // Not a subscription invoice, skip
            return WebhookProcessingResult.Skipped("Not a subscription invoice");
        }

        var subscriptionEvent = new SubscriptionEvent
        {
            SubscriptionId = invoice.SubscriptionId,
            EventType = SubscriptionEventType.PaymentFailed,
            NewStatus = SubscriptionStatus.PastDue,
            Provider = Provider
        };

        await _subscriptionManager.HandleSubscriptionEventAsync(subscriptionEvent, ct).ConfigureAwait(false);

        return WebhookProcessingResult.Success(
            webhookEvent.EventType,
            $"Invoice payment failed for subscription {invoice.SubscriptionId}");
    }

    private Task<WebhookProcessingResult> HandleChargeRefundedAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken ct)
    {
        if (webhookEvent.Data is Charge charge)
        {
            _logger.LogInformation(
                "Charge {ChargeId} refunded: {RefundedAmount}",
                charge.Id,
                charge.AmountRefunded);
        }

        return Task.FromResult(WebhookProcessingResult.Success(
            webhookEvent.EventType,
            "Refund event logged"));
    }

    private async Task<WebhookProcessingResult> HandleCheckoutSessionExpiredAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken ct)
    {
        if (webhookEvent.Data is not Session session)
        {
            return WebhookProcessingResult.Failed("Session data is null");
        }

        // Inform consumers that the session expired.
        await _eventPublisher.PublishAsync(new CheckoutSessionExpiredEvent
        {
            SessionId = session.Id,
            Provider = Provider.ToString(),
            ExpiredAt = DateTime.UtcNow
        }, ct).ConfigureAwait(false);

        return WebhookProcessingResult.Success(
            StripeConstants.EventTypes.CheckoutSessionExpired,
            $"Session {session.Id} expiration processed");
    }

    private WebhookProcessingResult HandleUnknownEvent(PaymentWebhookEvent webhookEvent)
    {
        _logger.LogInformation(
            "Unhandled Stripe event type {EventType} received. EventId: {EventId}",
            webhookEvent.EventType,
            webhookEvent.EventId);

        return WebhookProcessingResult.Skipped($"Unhandled event type: {webhookEvent.EventType}");
    }

    private void TrackWebhookEvent(PaymentWebhookEvent webhookEvent, WebhookProcessingResult result)
    {
        _telemetry.TrackEvent("WebhookProcessed", new Dictionary<string, string>
        {
            ["Provider"] = Provider.ToString(),
            ["EventType"] = webhookEvent.EventType,
            ["EventId"] = webhookEvent.EventId,
            ["Processed"] = result.Processed.ToString()
        });
    }
}
