using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Haworks.BuildingBlocks.Authentication;

public static class JwksAuthenticationExtensions
{
    public static IServiceCollection AddJwksAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JwksOptions>()
            .Bind(configuration.GetSection(JwksOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient("JwksKeyFetch");
        services.AddSingleton<JwksKeyCache>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearer =>
            {
                var jwksSection = configuration.GetSection(JwksOptions.SectionName);
                var issuer = jwksSection["Issuer"] ?? throw new InvalidOperationException("JwksOptions:Issuer required");
                var audience = jwksSection["Audience"] ?? throw new InvalidOperationException("JwksOptions:Audience required");

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                    NameClaimType = "sub",
                };
            });

        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(sp =>
            new PostConfigureJwksBearerOptions(
                sp.GetRequiredService<JwksKeyCache>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("JwksKeyResolver")));

        services.AddHostedService<JwksWarmupHostedService>();

        return services;
    }

    internal sealed class JwksKeyCache
    {
        private readonly JwksOptions _opts;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<JwksKeyCache> _logger;
        private volatile IList<SecurityKey> _keys = Array.Empty<SecurityKey>();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private DateTime _lastRefresh = DateTime.MinValue;

        public JwksKeyCache(
            IOptions<JwksOptions> opts,
            IHttpClientFactory httpClientFactory,
            ILogger<JwksKeyCache> logger)
        {
            _opts = opts.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public IList<SecurityKey> Keys => _keys;

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMinutes(1))
                return;

            if (!await _lock.WaitAsync(TimeSpan.FromSeconds(10), ct))
                return;

            try
            {
                if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMinutes(1))
                    return;

                var client = _httpClientFactory.CreateClient("JwksKeyFetch");
                var json = await client.GetStringAsync(_opts.JwksUri, ct);
                var jwks = new JsonWebKeySet(json);
                _keys = jwks.GetSigningKeys();
                _lastRefresh = DateTime.UtcNow;
                _logger.LogInformation("JWKS refreshed — {Count} signing key(s) cached", _keys.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JWKS refresh failed; keeping {Count} cached key(s)", _keys.Count);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    private sealed class PostConfigureJwksBearerOptions(
        JwksKeyCache cache,
        ILogger logger) : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            options.TokenValidationParameters.IssuerSigningKeyResolver =
                (token, securityToken, kid, parameters) => ResolveKeys(kid);
        }

        private IEnumerable<SecurityKey> ResolveKeys(string? kid)
        {
            var keys = cache.Keys;
            if (keys.Count == 0)
            {
                logger.LogWarning("JWKS cache is empty — no signing keys available");
                return Array.Empty<SecurityKey>();
            }

            if (!string.IsNullOrEmpty(kid))
            {
                var matched = keys
                    .Where(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal))
                    .ToArray();
                if (matched.Length > 0) return matched;
            }

            return keys;
        }
    }

    internal sealed class JwksWarmupHostedService(
        JwksKeyCache cache,
        ILogger<JwksWarmupHostedService> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await cache.RefreshAsync(cancellationToken);
                logger.LogInformation("JWKS cache pre-warmed successfully");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JWKS pre-warm failed — keys will be fetched on first request");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
