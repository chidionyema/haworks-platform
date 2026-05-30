using FluentValidation;

namespace Haworks.BuildingBlocks.Common;

/// <summary>
/// Shared FluentValidation rules for ISO 4217 currency codes, so every service
/// validates currency the same way (see <see cref="Money.IsValidCurrencyCode"/>).
/// </summary>
public static class CurrencyValidationExtensions
{
    /// <summary>
    /// Requires the property to be a valid ISO 4217 currency code
    /// (exactly three ASCII uppercase letters, non-empty).
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeValidCurrency<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder
            .Must(Money.IsValidCurrencyCode)
            .WithMessage("Currency must be a valid 3-letter ISO 4217 code (e.g., USD, EUR).");
}
