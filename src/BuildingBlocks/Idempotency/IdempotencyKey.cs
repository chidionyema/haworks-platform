using System.Security.Cryptography;
using System.Text;

namespace Haworks.BuildingBlocks.Idempotency;

/// <summary>
/// Deterministic, user-scoped idempotency-key derivation.
///
/// Mandate (per .claude/rules/dotnet-clean-arch.md "MANDATORY: SHA-256
/// Idempotency"): keys MUST be namespaced by UserId so a different user
/// replaying with the same client-side nonce can't collide with another
/// user's in-flight or recently-completed operation.
///
/// Use at every public API ingress that has retry semantics: Checkout
/// start, Order create, Payment session create. Same (userId, operation,
/// components) ALWAYS produces the same key — that's the property that
/// makes retries deterministically dedupe.
/// </summary>
public static class IdempotencyKey
{
    /// <summary>
    /// Derive a deterministic key from the user, operation name, and
    /// caller-supplied components. Components are sorted before hashing
    /// so caller order doesn't affect the result.
    /// </summary>
    /// <param name="userId">Authenticated user id; required.</param>
    /// <param name="operation">Stable operation tag, e.g. "checkout" or "payment.session".</param>
    /// <param name="components">
    /// Stable parts of the request that identify "the same operation":
    /// total amount, sorted item ids+quantities, an optional client nonce, etc.
    /// Pass them all — sorting is internal.
    /// </param>
    /// <returns>URL-safe base64 of the SHA-256 hash.</returns>
    public static string Derive(string userId, string operation, params string[] components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var sb = new StringBuilder(256)
            .Append(userId).Append('|')
            .Append(operation).Append('|');

        foreach (var c in components.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(c ?? string.Empty).Append('|');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        // URL-safe base64 so the key fits in headers, query strings, log lines.
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
