namespace Haworks.BuildingBlocks.Vault;
using Haworks.BuildingBlocks.Common;

public static class VaultErrors
{
    public static readonly Error ConnectionFailed = new("Vault.ConnectionFailed", "Failed to connect to Vault");
    public static readonly Error CredentialRefreshFailed = new("Vault.CredentialRefreshFailed", "Failed to refresh credentials");
    public static readonly Error CertificateValidationFailed = new("Vault.CertificateValidationFailed", "Certificate validation failed");
}
