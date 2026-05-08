using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain.Interfaces;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Contracts.Payments;
using Microsoft.Extensions.Logging;
using Polly;
using Stripe;
using NativeStripeSubService = Stripe.SubscriptionService;
using DomainSubscription = Haworks.Payments.Domain.Subscription;

namespace Haworks.Payments.Infrastructure.Stripe;

public sealed class StripeSubscriptionManager(
    IPaymentRepository paymentRepository,
    IStripeClientFactory clientFactory,
    IDomainEventPublisher eventPublisher,
    IResiliencePolicyFactory resiliencePolicyFactory) : ISubscriptionManager
{
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.Stripe);

    public async Task<SubscriptionStatusResult> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        var subscription = await paymentRepository.GetSubscriptionByUserIdAsync(userId, ct);
        if (subscription == null) return new SubscriptionStatusResult { IsActive = false, Provider = PaymentProvider.Stripe };
        return new SubscriptionStatusResult { IsActive = subscription.IsActive, SubscriptionId = subscription.ProviderSubscriptionId, Status = subscription.Status, Provider = PaymentProvider.Stripe };
    }

    public async Task<bool> CancelAsync(string subscriptionId, bool immediate = false, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetClientAsync(token);
            var service = new NativeStripeSubService(client);
            if (immediate) await service.CancelAsync(subscriptionId, cancellationToken: token);
            else await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }, cancellationToken: token);
            return true;
        }, new Context(), ct);
    }

    public async Task<bool> ResumeAsync(string subscriptionId, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetClientAsync(token);
            var service = new NativeStripeSubService(client);
            await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = false }, cancellationToken: token);
            return true;
        }, new Context(), ct);
    }

    public async Task HandleSubscriptionEventAsync(SubscriptionEvent subscriptionEvent, CancellationToken ct = default)
    {
        var existing = await paymentRepository.GetSubscriptionByProviderIdAsync(subscriptionEvent.SubscriptionId, ct);
        if (subscriptionEvent.EventType == SubscriptionEventType.Created && existing == null)
        {
            var newSub = DomainSubscription.Create(subscriptionEvent.UserId!, PaymentProvider.Stripe, subscriptionEvent.SubscriptionId, subscriptionEvent.PlanId!, DateTime.UtcNow, subscriptionEvent.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1));
            await paymentRepository.AddSubscriptionAsync(newSub, ct);
            await eventPublisher.PublishAsync(new SubscriptionStartedEvent { SubscriptionId = newSub.ProviderSubscriptionId, UserId = newSub.UserId, PlanId = newSub.PlanId, Provider = PaymentProvider.Stripe, CurrentPeriodEnd = newSub.ExpiresAt }, ct);
        }
        await paymentRepository.SaveChangesAsync(ct);
    }
}
