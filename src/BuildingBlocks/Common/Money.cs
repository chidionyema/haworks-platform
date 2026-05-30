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

    /// <summary>
    /// Validates an ISO 4217 currency code: exactly three ASCII uppercase letters.
    /// This is the single source of truth for currency validation across all services.
    /// </summary>
    public static bool IsValidCurrencyCode(string? currencyCode) =>
        currencyCode is { Length: 3 } && currencyCode.All(static c => c is >= 'A' and <= 'Z');

    /// <summary>Convert from major units (e.g. 39.99) to Money in minor units (e.g. 3999 cents).</summary>
    /// <exception cref="ArgumentException">The currency code is not a valid ISO 4217 code.</exception>
    /// <exception cref="OverflowException">The amount exceeds the range of a 64-bit minor-unit value.</exception>
    public static Money FromMajorUnits(decimal majorUnits, string currencyCode)
    {
        if (!IsValidCurrencyCode(currencyCode))
        {
            throw new ArgumentException($"Invalid ISO 4217 currency code: '{currencyCode}'.", nameof(currencyCode));
        }

        var multiplier = GetMultiplier(currencyCode);
        var scaled = Math.Round(majorUnits * multiplier, 0, MidpointRounding.AwayFromZero);
        if (scaled > long.MaxValue || scaled < long.MinValue)
        {
            throw new OverflowException($"Amount {majorUnits} {currencyCode} exceeds the supported minor-unit range.");
        }

        return new Money((long)scaled, currencyCode);
    }

    /// <summary>
    /// Non-throwing variant of <see cref="FromMajorUnits"/>. Returns false for an invalid
    /// currency code or an amount outside the 64-bit minor-unit range.
    /// </summary>
    public static bool TryFromMajorUnits(decimal majorUnits, string? currencyCode, out Money money)
    {
        money = default;
        if (!IsValidCurrencyCode(currencyCode))
        {
            return false;
        }

        var multiplier = GetMultiplier(currencyCode!);
        try
        {
            // The multiply itself can overflow the decimal type before the long-range
            // check runs, so the conversion is wrapped to keep this method non-throwing.
            var scaled = Math.Round(majorUnits * multiplier, 0, MidpointRounding.AwayFromZero);
            if (scaled > long.MaxValue || scaled < long.MinValue)
            {
                return false;
            }

            money = new Money((long)scaled, currencyCode!);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Convert to major units for display (e.g. 3999 cents → 39.99).</summary>
    public decimal ToMajorUnits()
    {
        var multiplier = GetMultiplier(CurrencyCode);
        return Math.Round((decimal)MinorUnits / multiplier, GetExponent(CurrencyCode), MidpointRounding.AwayFromZero);
    }

    public override string ToString() => $"{ToMajorUnits()} {CurrencyCode}";
}
