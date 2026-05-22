namespace Haworks.BuildingBlocks.Resilience;

/// <summary>
/// Centralized, config-driven timeout budgets for every HttpClient and
/// SemaphoreSlim gate in the platform. Bind from <c>HttpClientTimeouts</c>
/// section in appsettings / env vars. Every property has a sensible
/// default matching the previously-hardcoded value — backward-compatible.
/// </summary>
public sealed class HttpClientTimeoutOptions
{
    public const string SectionName = "HttpClientTimeouts";

    // ── BuildingBlocks / cross-cutting ──────────────────────────────
    public int JwksStartupSeconds { get; init; } = 10;
    public int VaultBootstrapSeconds { get; init; } = 30;
    public int HybridCacheLockSeconds { get; init; } = 30;

    // ── Vault credential gates ──────────────────────────────────────
    public int VaultClientGateSeconds { get; init; } = 60;
    public int CredentialStoreLockSeconds { get; init; } = 30;

    // ── BFF → backend service calls ─────────────────────────────────
    public int BffBackendSeconds { get; init; } = 15;
    public int BffCatalogDemoSeconds { get; init; } = 15;
    public int BffTokenProviderLockSeconds { get; init; } = 15;

    // ── Per-service HttpClient timeouts ─────────────────────────────
    public int SearchCatalogSeconds { get; init; } = 5;
    public int WebhooksDispatchSeconds { get; init; } = 10;
    public int IdentityVaultProbeSeconds { get; init; } = 2;
    public int PayPalApiSeconds { get; init; } = 30;
    public int StripeClientLockSeconds { get; init; } = 10;
    public int PayPalTokenLockSeconds { get; init; } = 30;
    public int LocationNominatimSeconds { get; init; } = 15;
}
