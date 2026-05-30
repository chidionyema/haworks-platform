using FluentValidation;
using FluentValidation.TestHelper;
using Haworks.BuildingBlocks.Common;
using Xunit;

namespace Haworks.BuildingBlocks.Unit.Common;

public sealed class CurrencyValidationExtensionsTests
{
    private sealed record Holder(string? Currency);

    private sealed class HolderValidator : AbstractValidator<Holder>
    {
        public HolderValidator() => RuleFor(x => x.Currency).MustBeValidCurrency();
    }

    private readonly HolderValidator _validator = new();

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("JPY")]
    [InlineData("KWD")]
    public void Accepts_wellformed_currency(string code) =>
        _validator.TestValidate(new Holder(code)).ShouldNotHaveValidationErrorFor(x => x.Currency);

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_malformed_currency(string? code) =>
        _validator.TestValidate(new Holder(code)).ShouldHaveValidationErrorFor(x => x.Currency);
}
