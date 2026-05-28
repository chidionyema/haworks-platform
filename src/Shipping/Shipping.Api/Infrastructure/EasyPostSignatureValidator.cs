using System.Security.Cryptography;
using System.Text;

namespace Haworks.Shipping.Api.Infrastructure;

/// <summary>
/// Validates an EasyPost webhook signature header per EasyPost's documented
/// scheme: the <c>X-EasyPost-Signature</c> header contains an HMAC-SHA256
/// signature of the raw payload using the webhook secret as the HMAC key.
/// </summary>
public static class EasyPostSignatureValidator
{
    /// <summary>
    /// Validates the EasyPost webhook signature.
    /// </summary>
    /// <param name="rawPayload">The raw JSON payload from the webhook</param>
    /// <param name="signatureHeader">The value from X-EasyPost-Signature header</param>
    /// <param name="webhookSecret">The configured webhook secret</param>
    /// <returns>True if the signature is valid, false otherwise</returns>
    public static bool TryValidate(
        string rawPayload,
        string signatureHeader,
        string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(rawPayload) ||
            string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(webhookSecret))
        {
            return false;
        }

        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
            var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

            // EasyPost may prefix with "sha256=" or just send the hash
            var providedSignature = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
                ? signatureHeader[7..].ToLowerInvariant()
                : signatureHeader.ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(providedSignature));
        }
        catch
        {
            return false;
        }
    }
}