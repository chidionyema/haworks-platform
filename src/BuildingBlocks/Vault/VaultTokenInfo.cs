namespace Haworks.BuildingBlocks.Vault;

public sealed record VaultTokenInfo(int TtlSeconds, string LeaseId, bool Renewable);
