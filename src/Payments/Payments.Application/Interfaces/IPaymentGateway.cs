using Haworks.Contracts.Payments;

namespace Haworks.Payments.Application.Interfaces;

/// <summary>
/// Main facade for payment operations. Provides a single entry point
/// for all payment-related functionality, delegating to the configured provider.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// The currently active payment provider.
    /// </summary>
    PaymentProvider ActiveProvider { get; }

    /// <summary>
    /// Checkout session operations (create, retrieve, expire).
    /// </summary>
    ICheckoutSessionService Checkout { get; }

    /// <summary>
    /// Subscription lifecycle management.
    /// </summary>
    ISubscriptionManager Subscriptions { get; }

    /// <summary>
    /// Refund processing.
    /// </summary>
    IRefundService Refunds { get; }

    /// <summary>
    /// Webhook validation and processing.
    /// </summary>
    IWebhookProcessor Webhooks { get; }

    /// <summary>
    /// Checks the health of the active payment provider.
    /// </summary>
    Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Health status of a payment provider.
/// </summary>
public record ProviderHealthStatus
{
    public required bool IsHealthy { get; init; }
    public string? Message { get; init; }
    public PaymentProvider Provider { get; init; }
}
