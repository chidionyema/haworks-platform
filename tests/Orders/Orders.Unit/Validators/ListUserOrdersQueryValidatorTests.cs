using FluentValidation.TestHelper;
using Haworks.Orders.Application.Queries;
using Xunit;

namespace Haworks.Orders.Unit.Validators;

public class ListUserOrdersQueryValidatorTests
{
    private readonly ListUserOrdersQueryValidator _validator = new();

    [Theory]
    [InlineData("", false)]      // Empty UserId
    [InlineData(null, false)]    // Null UserId
    [InlineData("valid-user", true)]
    [InlineData("user123", true)]
    [InlineData("  ", false)]    // Whitespace only
    public void Validate_UserId_ReturnsExpectedResult(string? userId, bool isValid)
    {
        var query = CreateValidQuery() with { UserId = userId! };
        var result = _validator.TestValidate(query);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.UserId);
        else
            result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Theory]
    [InlineData(-1, false)]  // Negative skip
    [InlineData(0, true)]    // Valid skip (start from beginning)
    [InlineData(10, true)]   // Valid skip
    [InlineData(1000, true)] // Large skip (valid)
    public void Validate_Skip_ReturnsExpectedResult(int skip, bool isValid)
    {
        var query = CreateValidQuery() with { Skip = skip };
        var result = _validator.TestValidate(query);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.Skip);
        else
            result.ShouldHaveValidationErrorFor(x => x.Skip);
    }

    [Theory]
    [InlineData(0, false)]    // Zero take (no records)
    [InlineData(-1, false)]   // Negative take
    [InlineData(1, true)]     // Minimum valid take
    [InlineData(50, true)]    // Valid take
    [InlineData(100, true)]   // Maximum valid take
    [InlineData(101, false)]  // Exceeds maximum
    [InlineData(1000, false)] // Way over maximum
    public void Validate_Take_ReturnsExpectedResult(int take, bool isValid)
    {
        var query = CreateValidQuery() with { Take = take };
        var result = _validator.TestValidate(query);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.Take);
        else
            result.ShouldHaveValidationErrorFor(x => x.Take);
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
        var query = new ListUserOrdersQuery(
            UserId: "",  // Invalid - empty
            Skip: -1,    // Invalid - negative
            Take: 0      // Invalid - zero
        );

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.UserId);
        result.ShouldHaveValidationErrorFor(x => x.Skip);
        result.ShouldHaveValidationErrorFor(x => x.Take);
    }

    [Fact]
    public void Validate_BoundaryValues_BehavesCorrectly()
    {
        // Test minimum valid values
        var queryMin = new ListUserOrdersQuery(
            UserId: "u",
            Skip: 0,
            Take: 1
        );
        var resultMin = _validator.TestValidate(queryMin);
        resultMin.ShouldNotHaveAnyValidationErrors();

        // Test maximum valid values
        var queryMax = new ListUserOrdersQuery(
            UserId: "user123",
            Skip: int.MaxValue, // No upper limit on skip
            Take: 100           // Maximum take
        );
        var resultMax = _validator.TestValidate(queryMax);
        resultMax.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(1, 1)]    // First page, 1 item
    [InlineData(0, 10)]   // First page, 10 items
    [InlineData(10, 20)]  // Second page, 20 items
    [InlineData(100, 50)] // Skip many, take 50
    public void Validate_CommonPaginationScenarios_ShouldBeValid(int skip, int take)
    {
        var query = new ListUserOrdersQuery(
            UserId: "user123",
            Skip: skip,
            Take: take
        );

        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    private static ListUserOrdersQuery CreateValidQuery() => new(
        UserId: "user123",
        Skip: 0,
        Take: 20
    );
}