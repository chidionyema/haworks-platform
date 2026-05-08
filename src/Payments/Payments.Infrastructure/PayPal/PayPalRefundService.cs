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

namespace Haworks.Payments.Infrastructure.PayPal;

internal sealed class PayPalRefundService(
    IPayPalClientFactory clientFactory,
    IPaymentRepository paymentRepository,
    IDomainEventPublisher eventPublisher,
    IResiliencePolicyFactory resiliencePolicyFactory) : IRefundService
{
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.Default);

    public async Task<RefundResult> CreateRefundAsync(RefundRequest request, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var refundReq = new PayPalRefundRequest();
            if (request.AmountCents.HasValue) refundReq.Amount = new PayPalRefundAmount { CurrencyCode = request.Currency ?? "USD", Value = (request.AmountCents.Value / 100m).ToString("F2") };
            
            var response = await client.PostAsJsonAsync(PayPalEndpoints.RefundCapture(request.TransactionId), refundReq, PayPalJsonOptions.Default, token);
            if (!response.IsSuccessStatusCode) return new RefundResult { RefundId = string.Empty, Status = RefundStatus.Failed, Provider = PaymentProvider.PayPal };
            
            var refund = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>(PayPalJsonOptions.Default, token);
            var payment = await paymentRepository.GetByProviderTransactionIdAsync(request.TransactionId, token);
            if (payment != null) await eventPublisher.PublishAsync(new RefundIssuedEvent { PaymentId = payment.Id, OrderId = payment.OrderId, RefundId = refund!.Id!, AmountCents = request.AmountCents ?? 0, Currency = payment.Currency, Provider = PaymentProvider.PayPal }, token);
            
            return new RefundResult { RefundId = refund!.Id!, Status = RefundStatus.Succeeded, Provider = PaymentProvider.PayPal };
        }, new Context(), ct);
    }

    public async Task<RefundResult> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetAuthenticatedClientAsync(token);
            var response = await client.GetAsync(PayPalEndpoints.GetRefund(refundId), token);
            if (!response.IsSuccessStatusCode) return new RefundResult { RefundId = refundId, Status = RefundStatus.Failed, Provider = PaymentProvider.PayPal };
            var refund = await response.Content.ReadFromJsonAsync<PayPalRefundResponse>(PayPalJsonOptions.Default, token);
            return new RefundResult { RefundId = refund!.Id!, Status = RefundStatus.Succeeded, Provider = PaymentProvider.PayPal };
        }, new Context(), ct);
    }
}
