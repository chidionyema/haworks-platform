namespace Haworks.BuildingBlocks.Common;

/// <summary>
/// Canonical monetary value: amount in minor units (cents/pence) + ISO 4217 currency code.
/// Use this for all new financial fields. Existing decimal fields will migrate to this.
/// </summary>
public readonly record struct Money(long MinorUnits, string CurrencyCode)
{
    /// <summary>
    /// Returns the exponent (number of minor unit digits) for a currency.
    /// Most currencies use 2 (cents). JPY/KRW use 0. KWD/BHD/OMR use 3.
    /// </summary>
    public static int GetExponent(string currencyCode) => currencyCode?.ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "ISK" => 0,
        "KWD" or "BHD" or "OMR" => 3,
        _ => 2
    };

    /// <summary>Multiplier to convert major units to minor units for this currency.</summary>
    public static long GetMultiplier(string currencyCode) =>
        (long)Math.Pow(10, GetExponent(currencyCode));

    /// <summary>Convert from major units (e.g. 39.99) to Money in minor units (e.g. 3999 cents).</summary>
    public static Money FromMajorUnits(decimal majorUnits, string currencyCode)
    {
        var multiplier = GetMultiplier(currencyCode);
        var minorUnits = (long)Math.Round(majorUnits * multiplier, 0, MidpointRounding.AwayFromZero);
        return new Money(minorUnits, currencyCode);
    }

    /// <summary>Convert to major units for display (e.g. 3999 cents → 39.99).</summary>
    public decimal ToMajorUnits()
    {
        var multiplier = GetMultiplier(CurrencyCode);
        return Math.Round((decimal)MinorUnits / multiplier, GetExponent(CurrencyCode), MidpointRounding.AwayFromZero);
    }

    public override string ToString() => $"{ToMajorUnits()} {CurrencyCode}";
}
