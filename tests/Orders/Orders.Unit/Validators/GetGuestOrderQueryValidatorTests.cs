using FluentValidation.TestHelper;
using Haworks.Orders.Application.Queries;
using Xunit;

namespace Haworks.Orders.Unit.Validators;

public class GetGuestOrderQueryValidatorTests
{
    private readonly GetGuestOrderQueryValidator _validator = new();

    [Theory]
    [InlineData("", false)]      // Empty token
    [InlineData(null, false)]    // Null token
    [InlineData("valid-token", true)]
    [InlineData("token-123", true)]
    [InlineData("  ", false)]    // Whitespace only
    public void Validate_Token_ReturnsExpectedResult(string? token, bool isValid)
    {
        var query = CreateValidQuery() with { Token = token! };
        var result = _validator.TestValidate(query);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.Token);
        else
            result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Fact]
    public void Validate_Token_TooLong_ShouldHaveError()
    {
        var longToken = new string('a', 501); // Exceeds 500 character limit
        var query = CreateValidQuery() with { Token = longToken };
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Fact]
    public void Validate_Token_AtMaxLength_ShouldNotHaveError()
    {
        var maxLengthToken = new string('a', 500); // Exactly 500 characters
        var query = CreateValidQuery() with { Token = maxLengthToken };
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Token);
    }

    [Theory]
    [InlineData("invalid-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("valid@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("test@domain", false)] // Missing TLD
    [InlineData("@example.com", false)] // Missing local part
    [InlineData("test@", false)] // Missing domain
    [InlineData("  ", false)] // Whitespace only
    public void Validate_Email_ReturnsExpectedResult(string? email, bool isValid)
    {
        var query = CreateValidQuery() with { Email = email! };
        var result = _validator.TestValidate(query);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.Email);
        else
            result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_Email_TooLong_ShouldHaveError()
    {
        // Create an email that exceeds 254 characters
        var longEmail = $"{new string('a', 240)}@example.com"; // 252 chars total
        var query = CreateValidQuery() with { Email = longEmail };
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Email); // Should be valid at 252 chars

        var tooLongEmail = $"{new string('a', 242)}@example.com"; // 255 chars total
        var queryTooLong = CreateValidQuery() with { Email = tooLongEmail };
        var resultTooLong = _validator.TestValidate(queryTooLong);
        resultTooLong.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_Email_AtMaxLength_ShouldNotHaveError()
    {
        // Create an email that is exactly 254 characters
        var maxLengthEmail = $"{new string('a', 240)}@example.com"; // 254 chars exactly
        var query = CreateValidQuery() with { Email = maxLengthEmail };
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_ValidQuery_ShouldNotHaveAnyErrors()
    {
        var query = CreateValidQuery();
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AllFieldsInvalid_ShouldHaveAllErrors()
    {
        var query = new GetGuestOrderQuery(
            Token: "", // Invalid - empty
            Email: "invalid-email" // Invalid - not email format
        );

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Token);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    private static GetGuestOrderQuery CreateValidQuery() => new(
        Token: "valid-guest-token-123",
        Email: "guest@example.com"
    );
}