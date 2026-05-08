namespace Haworks.Payments.Application.Interfaces;

public interface IPayPalClientFactory
{
    string BaseUrl { get; }
    Task<HttpClient> GetAuthenticatedClientAsync(CancellationToken ct = default);
}
