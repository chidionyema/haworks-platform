using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Pricing.Domain.Entities;

/// <summary>
/// Configurable tax rate for a country/state jurisdiction.
/// </summary>
public sealed class TaxRate : AuditableEntity
{
    private TaxRate() { }

    public string CountryCode { get; private set; } = string.Empty;
    public string? StateCode { get; private set; }
    public decimal CombinedRate { get; private set; }
    public decimal StateRate { get; private set; }
    public decimal CountyRate { get; private set; }
    public decimal LocalRate { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public string? Notes { get; private set; }

    public static TaxRate Create(
        string countryCode,
        string? stateCode,
        decimal combinedRate,
        decimal stateRate = 0,
        decimal countyRate = 0,
        decimal localRate = 0,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null,
        string? notes = null)
    {
        // H1 Fix: Validate rate bounds (0-50% reasonable upper bound for any jurisdiction)
        if (combinedRate < 0 || combinedRate > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(combinedRate), "Combined tax rate must be between 0 and 0.5 (50%)");
        if (stateRate < 0 || stateRate > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(stateRate), "State rate must be between 0 and 0.5");
        if (countyRate < 0 || countyRate > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(countyRate), "County rate must be between 0 and 0.5");
        if (localRate < 0 || localRate > 0.5m)
            throw new ArgumentOutOfRangeException(nameof(localRate), "Local rate must be between 0 and 0.5");

        // M3 Fix: Validate sub-rate consistency
        var subRateSum = stateRate + countyRate + localRate;
        if (subRateSum > 0 && Math.Abs(combinedRate - subRateSum) > 0.001m)
            throw new ArgumentException($"CombinedRate ({combinedRate}) must equal sum of sub-rates ({subRateSum})");

        return new TaxRate
        {
            CountryCode = countryCode.ToUpperInvariant(),
            StateCode = stateCode?.ToUpperInvariant(),
            CombinedRate = combinedRate,
            StateRate = stateRate,
            CountyRate = countyRate,
            LocalRate = localRate,
            EffectiveFrom = effectiveFrom ?? DateTimeOffset.UtcNow,
            EffectiveTo = effectiveTo,
            Notes = notes,
        };
    }
}
