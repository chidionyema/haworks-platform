using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain.Interfaces;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Contracts.Payments;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Json;
using System.Text.Json;
using DomainSubscription = Haworks.Payments.Domain.Subscription;

namespace Haworks.Payments.Infrastructure.PayPal;

internal sealed class PayPalSubscriptionManager(
    IPaymentRepository paymentRepository,
    IDomainEventPublisher eventPublisher,
    IPayPalClientFactory clientFactory,
    IResiliencePolicyFactory resiliencePolicyFactory) : ISubscriptionManager
{
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.Default);

    public async Task<SubscriptionStatusResult> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var subscription = await paymentRepository.GetSubscriptionByUserIdAsync(userId, ct);
        if (subscription == null || subscription.Provider != PaymentProvider.PayPal) return new SubscriptionStatusResult { IsActive = false, Provider = PaymentProvider.PayPal };
        
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var response = await client.GetAsync(PayPalEndpoints.GetSubscription(subscription.ProviderSubscriptionId), token);
            if (!response.IsSuccessStatusCode) return new SubscriptionStatusResult { IsActive = false, Provider = PaymentProvider.PayPal };
            var paypalSub = await response.Content.ReadFromJsonAsync<PayPalSubscriptionResponse>(PayPalJsonOptions.Default, token);
            return new SubscriptionStatusResult { IsActive = paypalSub!.Status == "ACTIVE", SubscriptionId = paypalSub.Id, Status = paypalSub.Status == "ACTIVE" ? SubscriptionStatus.Active : SubscriptionStatus.Canceled, Provider = PaymentProvider.PayPal };
        }, new Context(), ct);
    }

    public async Task<bool> CancelAsync(string subscriptionId, bool immediate = false, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var response = await client.PostAsJsonAsync(PayPalEndpoints.CancelSubscription(subscriptionId), new { reason = "User requested" }, token);
            if (response.IsSuccessStatusCode)
            {
                var sub = await paymentRepository.GetSubscriptionByProviderIdAsync(subscriptionId, token);
                if (sub != null) { sub.Cancel(); await paymentRepository.SaveChangesAsync(token); }
                return true;
            }
            return false;
        }, new Context(), ct);
    }

    public async Task<bool> ResumeAsync(string subscriptionId, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var response = await client.PostAsJsonAsync(PayPalEndpoints.ActivateSubscription(subscriptionId), new { reason = "User resumed" }, token);
            if (response.IsSuccessStatusCode)
            {
                var sub = await paymentRepository.GetSubscriptionByProviderIdAsync(subscriptionId, token);
                if (sub != null) { sub.Activate(); await paymentRepository.SaveChangesAsync(token); }
                return true;
            }
            return false;
        }, new Context(), ct);
    }

    public async Task HandleSubscriptionEventAsync(SubscriptionEvent subscriptionEvent, CancellationToken ct = default)
    {
        var subscription = await paymentRepository.GetSubscriptionByProviderIdAsync(subscriptionEvent.SubscriptionId, ct);
        if (subscriptionEvent.EventType == SubscriptionEventType.Created && subscription == null)
        {
            var newSub = DomainSubscription.Create(subscriptionEvent.UserId!, PaymentProvider.PayPal, subscriptionEvent.SubscriptionId, subscriptionEvent.PlanId!, DateTime.UtcNow, subscriptionEvent.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1));
            await paymentRepository.AddSubscriptionAsync(newSub, ct);
            await eventPublisher.PublishAsync(new SubscriptionStartedEvent { SubscriptionId = newSub.ProviderSubscriptionId, UserId = newSub.UserId, PlanId = newSub.PlanId, Provider = PaymentProvider.PayPal, CurrentPeriodEnd = newSub.ExpiresAt }, ct);
        }
        await paymentRepository.SaveChangesAsync(ct);
    }
}
