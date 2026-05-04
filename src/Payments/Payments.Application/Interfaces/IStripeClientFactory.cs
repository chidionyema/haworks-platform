using Stripe;

namespace Haworks.Payments.Application.Interfaces;

public interface IStripeClientFactory
{
    Task<IStripeClient> GetClientAsync(CancellationToken ct = default);
}
