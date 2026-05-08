using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Json;
using System.Text.Json;

namespace Haworks.Payments.Infrastructure.PayPal;

internal sealed class PayPalCheckoutService(
    IPayPalClientFactory clientFactory,
    IResiliencePolicyFactory resiliencePolicyFactory) : ICheckoutSessionService, ISubscriptionService
{
    // Use the PayPal-specific resilience profile (longer initial delay,
    // higher CB threshold than the generic Default — PayPal is slower
    // and historically flaky enough to warrant the dedicated tuning).
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.PayPal);

    public async Task<CheckoutSessionResult> CreateSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var currency = !string.IsNullOrEmpty(request.Currency) ? request.Currency : "USD";
            var orderRequest = new PayPalOrderRequest
            {
                Intent = "CAPTURE",
                PurchaseUnits = new List<PayPalPurchaseUnit>
                {
                    new PayPalPurchaseUnit
                    {
                        ReferenceId = request.IdempotencyKey ?? Guid.NewGuid().ToString(),
                        Amount = new PayPalAmount { CurrencyCode = currency, Value = (request.LineItems.Sum(i => i.UnitAmountCents * i.Quantity) / 100m).ToString("F2") },
                        CustomId = request.Metadata?.GetValueOrDefault("orderId") ?? string.Empty
                    }
                },
                ApplicationContext = new PayPalApplicationContext { ReturnUrl = request.SuccessUrl, CancelUrl = request.CancelUrl }
            };

            var response = await client.PostAsJsonAsync(PayPalEndpoints.CheckoutOrders, orderRequest, PayPalJsonOptions.Default, token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"PayPal order failed: {response.StatusCode}");
            var order = await response.Content.ReadFromJsonAsync<PayPalOrder>(PayPalJsonOptions.Default, token);
            return new CheckoutSessionResult { SessionId = order!.Id!, SessionUrl = order.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href ?? string.Empty, Provider = PaymentProvider.PayPal };
        }, new Context(), ct);
    }

    public async Task<CheckoutSessionResult> CreateSubscriptionSessionAsync(CreateSubscriptionSessionRequest request, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var subRequest = new PayPalSubscriptionRequest { PlanId = request.PlanId, ApplicationContext = new PayPalApplicationContext { ReturnUrl = request.SuccessUrl, CancelUrl = request.CancelUrl } };
            var response = await client.PostAsJsonAsync(PayPalEndpoints.Subscriptions, subRequest, PayPalJsonOptions.Default, token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"PayPal sub failed: {response.StatusCode}");
            var sub = await response.Content.ReadFromJsonAsync<PayPalSubscription>(PayPalJsonOptions.Default, token);
            return new CheckoutSessionResult { SessionId = sub!.Id!, SessionUrl = sub.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href ?? string.Empty, Provider = PaymentProvider.PayPal };
        }, new Context(), ct);
    }

    public async Task<CheckoutSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var response = await client.GetAsync(PayPalEndpoints.GetOrder(sessionId), token);
            if (!response.IsSuccessStatusCode) return null;
            var order = await response.Content.ReadFromJsonAsync<PayPalOrder>(PayPalJsonOptions.Default, token);
            return new CheckoutSession
            {
                SessionId = order!.Id!,
                Status = order.Status == "COMPLETED" ? SessionStatus.Complete : SessionStatus.Open,
                AmountTotal = (long)(decimal.Parse(order.PurchaseUnits![0].Amount!.Value) * 100),
                Currency = order.PurchaseUnits[0].Amount!.CurrencyCode,
                Provider = PaymentProvider.PayPal
            };
        }, new Context(), ct);
    }

    public Task<bool> ExpireSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
}
