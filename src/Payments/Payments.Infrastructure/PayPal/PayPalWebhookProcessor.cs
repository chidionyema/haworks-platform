using Haworks.Payments.Application.Interfaces;
using Haworks.Contracts.Payments;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using System.Net.Http.Json;
using System.Text.Json;

namespace Haworks.Payments.Infrastructure.PayPal;

internal sealed class PayPalWebhookProcessor(
    IPaymentSessionProcessor paymentProcessor,
    IWebhookIdempotencyGuard idempotencyGuard,
    IOptions<PaymentProviderOptions> providerOptions) : IWebhookProcessor
{
    private readonly PayPalOptions _options = providerOptions.Value.PayPal;

    public PaymentProvider Provider => PaymentProvider.PayPal;

    public Task<WebhookValidationResult> ValidateAndParseAsync(string payload, string signature, CancellationToken ct = default)
    {
        try
        {
            var paypalEvent = JsonSerializer.Deserialize<PayPalWebhookEvent>(payload, PayPalJsonOptions.Default);
            var webhookEvent = new PaymentWebhookEvent 
            { 
                EventId = paypalEvent!.Id!, 
                EventType = paypalEvent.EventType!, 
                Provider = PaymentProvider.PayPal, 
                CreatedAt = DateTime.UtcNow,
                Data = paypalEvent.Resource 
            };
            return Task.FromResult(WebhookValidationResult.Success(webhookEvent));
        }
        catch { return Task.FromResult(WebhookValidationResult.Failure("Invalid PayPal webhook")); }
    }

    public async Task<WebhookProcessingResult> ProcessEventAsync(PaymentWebhookEvent webhookEvent, CancellationToken ct = default)
    {
        if (await idempotencyGuard.IsAlreadyProcessedAsync(Provider, webhookEvent.EventId, ct)) return WebhookProcessingResult.Skipped("Already processed");

        if (webhookEvent.EventType == PayPalEventTypes.PaymentCaptureCompleted)
        {
            var res = (JsonElement)webhookEvent.Data!;
            var sessionEvent = new PaymentSessionEvent 
            { 
                SessionId = res.GetProperty("id").GetString()!, 
                TransactionId = res.GetProperty("id").GetString()!, 
                Provider = PaymentProvider.PayPal, 
                Mode = SessionMode.Payment,
                Currency = "USD",
                AmountTotal = (long)(decimal.Parse(res.GetProperty("amount").GetProperty("value").GetString()!) * 100) 
            };
            await paymentProcessor.HandleCompletedSessionAsync(sessionEvent, ct);
        }

        await idempotencyGuard.MarkProcessedAsync(Provider, webhookEvent.EventId, webhookEvent.EventType, ct);
        return WebhookProcessingResult.Success(webhookEvent.EventType, "Processed");
    }
}
