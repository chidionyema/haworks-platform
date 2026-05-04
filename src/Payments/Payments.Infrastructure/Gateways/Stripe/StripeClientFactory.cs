using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Haworks.Payments.Infrastructure.Gateways.Stripe;

public sealed class StripeClientFactory : IStripeClientFactory
{
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeClientFactory> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private IStripeClient? _cachedClient;

    public StripeClientFactory(
        IOptions<StripeOptions> options,
        ILogger<StripeClientFactory> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IStripeClient> GetClientAsync(CancellationToken ct = default)
    {
        if (_cachedClient != null) return _cachedClient;

        await _clientLock.WaitAsync(ct);
        try
        {
            if (_cachedClient != null) return _cachedClient;

            var apiKey = _options.Value.SecretKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Stripe SecretKey is missing from configuration.");
            }

            var baseUrl = _options.Value.BaseUrl;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogInformation("Creating StripeClient with custom base URL: {BaseUrl}", baseUrl);
                _cachedClient = new StripeClient(apiKey, apiBase: baseUrl);
            }
            else
            {
                _cachedClient = new StripeClient(apiKey);
            }

            return _cachedClient;
        }
        finally
        {
            _clientLock.Release();
        }
    }
}
