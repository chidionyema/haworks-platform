using Haworks.Payments.Domain;

namespace Haworks.Payments.Application.Interfaces;

public interface ISubscriptionService
{
    Task<CheckoutSessionResult> CreateSubscriptionSessionAsync(CreateSubscriptionSessionRequest request, CancellationToken ct = default);
}

public record CreateSubscriptionSessionRequest(
    string UserId,
    string CustomerEmail,
    string PlanId,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    IDictionary<string, string>? Metadata = null);
