using Haworks.Payments.Application.Interfaces;
using Haworks.Payments.Infrastructure.Options;
using Haworks.BuildingBlocks.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polly;

namespace Haworks.Payments.Infrastructure.PayPal;

internal sealed class PayPalClientFactory : IPayPalClientFactory, IDisposable
{
    private readonly PayPalOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PayPalClientFactory> _logger;
    private readonly IAsyncPolicy _resiliencePolicy;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string BaseUrl => _options.BaseUrl;

    public PayPalClientFactory(
        IOptions<PaymentProviderOptions> providerOptions,
        IHttpClientFactory httpClientFactory,
        IResiliencePolicyFactory resiliencePolicyFactory,
        ILogger<PayPalClientFactory> logger)
    {
        _options = providerOptions?.Value?.PayPal ?? throw new ArgumentNullException(nameof(providerOptions));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePolicy = resiliencePolicyFactory.CreateCombinedPolicy(ResilienceOptions.Default);
    }

    public async Task<HttpClient> GetAuthenticatedClientAsync(CancellationToken ct = default)
    {
        if (IsTokenValid()) return CreateAuthenticatedClient(_accessToken!);

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (IsTokenValid()) return CreateAuthenticatedClient(_accessToken!);

            var tokenResponse = await _resiliencePolicy.ExecuteAsync(async (ctx, token) => await FetchOAuthTokenAsync(token), new Context(), ct);
            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            return CreateAuthenticatedClient(_accessToken);
        }
        finally { _tokenLock.Release(); }
    }

    private bool IsTokenValid() => _accessToken != null && DateTime.UtcNow < _tokenExpiry.Subtract(TokenExpiryBuffer);

    private HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = HttpClientTimeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<PayPalTokenResponse> FetchOAuthTokenAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        client.BaseAddress = new Uri(_options.BaseUrl);
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, PayPalEndpoints.OAuthToken)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"PayPal OAuth failed: {response.StatusCode}");
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PayPalTokenResponse>(responseBody, JsonOptions) 
            ?? throw new InvalidOperationException("Invalid PayPal token response");
    }

    public void Dispose() => _tokenLock.Dispose();
}
