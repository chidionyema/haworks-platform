using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Domain.Interfaces;
using Haworks.BuildingBlocks.Telemetry;
using Haworks.BuildingBlocks.Resilience;
using Haworks.Contracts.Payments;
using Microsoft.Extensions.Logging;
using Polly;
using Stripe;

namespace Haworks.Payments.Infrastructure.Stripe;

internal sealed class StripeRefundService(
    IStripeClientFactory clientFactory,
    IPaymentRepository paymentRepository,
    IDomainEventPublisher eventPublisher,
    IResiliencePolicyFactory resiliencePolicyFactory) : IRefundService
{
    private readonly IAsyncPolicy _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.Stripe);

    public async Task<RefundResult> CreateRefundAsync(RefundRequest request, CancellationToken ct = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async (ctx, token) =>
        {
            var client = await clientFactory.GetClientAsync(token);
            var service = new RefundService(client);
            var options = new RefundCreateOptions { PaymentIntent = request.TransactionId };
            if (request.AmountCents.HasValue) options.Amount = request.AmountCents.Value;
            
            var refund = await service.CreateAsync(options, cancellationToken: token);
            var payment = await paymentRepository.GetByProviderTransactionIdAsync(request.TransactionId, token);
            if (payment != null && refund.Status == "succeeded")
            {
                await eventPublisher.PublishAsync(new RefundIssuedEvent { PaymentId = payment.Id, OrderId = payment.OrderId, RefundId = refund.Id, AmountCents = refund.Amount, Currency = payment.Currency, Provider = PaymentProvider.Stripe }, token);
            }
            return new RefundResult { RefundId = refund.Id, Status = refund.Status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Failed, Provider = PaymentProvider.Stripe };
        }, new Context(), ct);
    }

    public async Task<RefundResult> GetRefundStatusAsync(string refundId, CancellationToken ct = default)
    {
        var client = await clientFactory.GetClientAsync(ct);
        var service = new RefundService(client);
        var refund = await service.GetAsync(refundId, cancellationToken: ct);
        return new RefundResult { RefundId = refund.Id, Status = refund.Status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Failed, Provider = PaymentProvider.Stripe };
    }
}
