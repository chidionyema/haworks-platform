using Haworks.Payments.Domain;

namespace Haworks.Payments.Application.Interfaces;

public interface ICheckoutSessionService
{
    Task<CheckoutSessionResult> CreateSessionAsync(CreateCheckoutSessionRequest request, CancellationToken ct = default);
}

public record CreateCheckoutSessionRequest(
    string UserId,
    string CustomerEmail,
    decimal Amount,
    string Currency,
    Guid OrderId,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    IDictionary<string, string>? Metadata = null);

public record CheckoutSessionResult(
    string SessionId,
    string SessionUrl,
    string? TransactionId,
    PaymentProvider Provider);
