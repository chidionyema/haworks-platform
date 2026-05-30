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
    /// The set of active ISO 4217 alphabetic currency codes accepted by the platform.
    /// Membership (not just 3-letter format) is enforced so a well-formed but nonexistent
    /// code like "ZZZ" is rejected rather than silently treated as a 2-decimal currency.
    /// </summary>
    private static readonly HashSet<string> Iso4217 = new(StringComparer.Ordinal)
    {
        "AED","AFN","ALL","AMD","ANG","AOA","ARS","AUD","AWG","AZN","BAM","BBD","BDT","BGN",
        "BHD","BIF","BMD","BND","BOB","BOV","BRL","BSD","BTN","BWP","BYN","BZD","CAD","CDF",
        "CHE","CHF","CHW","CLF","CLP","CNY","COP","COU","CRC","CUP","CVE","CZK","DJF","DKK",
        "DOP","DZD","EGP","ERN","ETB","EUR","FJD","FKP","GBP","GEL","GHS","GIP","GMD","GNF",
        "GTQ","GYD","HKD","HNL","HTG","HUF","IDR","ILS","INR","IQD","IRR","ISK","JMD","JOD",
        "JPY","KES","KGS","KHR","KMF","KPW","KRW","KWD","KYD","KZT","LAK","LBP","LKR","LRD",
        "LSL","LYD","MAD","MDL","MGA","MKD","MMK","MNT","MOP","MRU","MUR","MVR","MWK","MXN",
        "MXV","MYR","MZN","NAD","NGN","NIO","NOK","NPR","NZD","OMR","PAB","PEN","PGK","PHP",
        "PKR","PLN","PYG","QAR","RON","RSD","RUB","RWF","SAR","SBD","SCR","SDG","SEK","SGD",
        "SHP","SLE","SOS","SRD","SSP","STN","SVC","SYP","SZL","THB","TJS","TMT","TND","TOP",
        "TRY","TTD","TWD","TZS","UAH","UGX","USD","USN","UYI","UYU","UYW","UZS","VED","VES",
        "VND","VUV","WST","XAF","XCD","XOF","XPF","XSU","XUA","YER","ZAR","ZMW","ZWG",
        // Funds / supranational / precious-metal codes that may legitimately appear.
        "XDR","XAU","XAG","XPT","XPD","XBA","XBB","XBC","XBD",
    };

    /// <summary>
    /// Validates a currency code against the active ISO 4217 alphabetic code set.
    /// This is the single source of truth for currency validation across all services.
    /// </summary>
    public static bool IsValidCurrencyCode(string? currencyCode) =>
        currencyCode is not null && Iso4217.Contains(currencyCode);

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
