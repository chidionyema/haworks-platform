using System.Net;
using System.Text;
using FluentAssertions;
using Haworks.BffWeb.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Haworks.BffWeb.Unit.Authentication;

/// <summary>
/// Unit coverage for <see cref="BffServiceTokenProvider"/> — the money/identity-
/// adjacent piece that mints the service JWT checkout-svc requires. Previously
/// zero coverage anywhere in tests/.
/// </summary>
public sealed class BffServiceTokenProviderTests
{
    private const string BaseUrl = "http://identity-svc.test";
    private const string Secret = "shared-secret";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        // Real IHttpClientFactory hands back a fresh HttpClient per call (the
        // provider sets client.BaseAddress on every GetTokenAsync invocation,
        // which throws InvalidOperationException on an HttpClient that has
        // already sent a request) — sharing one HttpClient across calls here
        // would silently break the second-call-onward path.
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static IConfiguration BuildConfig(string? baseUrl = BaseUrl, string? secret = Secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:Identity:BaseUrl"] = baseUrl,
                ["ServiceAuth:SharedSecret"] = secret,
            })
            .Build();

    private static string Base64UrlEncode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Minimal unsigned ("alg":"none") JWT — enough for JwtSecurityTokenHandler.ReadJwtToken to parse an exp claim.</summary>
    private static string MakeJwt(DateTime expiryUtc)
    {
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var exp = new DateTimeOffset(DateTime.SpecifyKind(expiryUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var payload = Base64UrlEncode("{\"sub\":\"bff-web\",\"exp\":" + exp + "}");
        return $"{header}.{payload}.";
    }

    private static HttpResponseMessage TokenResponse(string jwt) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"token\":\"" + jwt + "\"}", Encoding.UTF8, "application/json"),
    };

    private static BffServiceTokenProvider CreateProvider(StubHandler handler, IConfiguration? config = null) =>
        new(new StubHttpClientFactory(handler), config ?? BuildConfig(), NullLogger<BffServiceTokenProvider>.Instance);

    [Fact]
    public async Task GetTokenAsync_returns_null_when_BaseUrl_is_missing()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not call out"));
        var provider = CreateProvider(handler, BuildConfig(baseUrl: null));

        var token = await provider.GetTokenAsync();

        token.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTokenAsync_returns_null_when_SharedSecret_is_missing()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not call out"));
        var provider = CreateProvider(handler, BuildConfig(secret: null));

        var token = await provider.GetTokenAsync();

        token.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTokenAsync_sends_X_Service_Secret_header_to_the_configured_service_token_endpoint()
    {
        var jwt = MakeJwt(DateTime.UtcNow.AddHours(1));
        var handler = new StubHandler(_ => TokenResponse(jwt));
        var provider = CreateProvider(handler);

        var token = await provider.GetTokenAsync();

        token.Should().Be(jwt);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v1/Authentication/service-token");
        handler.LastRequest!.Headers.GetValues("X-Service-Secret").Should().ContainSingle().Which.Should().Be(Secret);
    }

    [Fact]
    public async Task GetTokenAsync_returns_null_when_upstream_returns_a_non_success_status()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = CreateProvider(handler);

        var token = await provider.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_returns_null_when_the_upstream_call_throws()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var provider = CreateProvider(handler);

        var token = await provider.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_caches_the_token_and_does_not_call_out_again_before_expiry()
    {
        var jwt = MakeJwt(DateTime.UtcNow.AddHours(1));
        var handler = new StubHandler(_ => TokenResponse(jwt));
        var provider = CreateProvider(handler);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        first.Should().Be(jwt);
        second.Should().Be(jwt);
        handler.CallCount.Should().Be(1, "the second call should be served from cache");
    }

    [Fact]
    public async Task GetTokenAsync_refetches_once_the_cached_token_is_within_1_minute_of_expiry()
    {
        // Expiry 30s out is inside the provider's 1-minute refresh window, so
        // the cached value must not be reused on the second call.
        var jwt1 = MakeJwt(DateTime.UtcNow.AddSeconds(30));
        var jwt2 = MakeJwt(DateTime.UtcNow.AddHours(1));
        var calls = 0;
        var handler = new StubHandler(_ => TokenResponse(++calls == 1 ? jwt1 : jwt2));
        var provider = CreateProvider(handler);

        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        first.Should().Be(jwt1);
        second.Should().Be(jwt2);
        handler.CallCount.Should().Be(2);
    }
}
