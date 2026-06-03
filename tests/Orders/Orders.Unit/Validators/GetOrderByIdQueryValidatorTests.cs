using FluentValidation.TestHelper;
using Haworks.Orders.Application.Queries;
using Xunit;

namespace Haworks.Orders.Unit.Validators;

public class GetOrderByIdQueryValidatorTests
{
    private readonly GetOrderByIdQueryValidator _validator = new();

    [Fact]
    public void Validate_Id_Empty_ShouldHaveError()
    {
        var query = CreateValidQuery() with { Id = Guid.Empty };
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Validate_Id_Valid_ShouldNotHaveError()
    {
        var query = CreateValidQuery() with { Id = Guid.NewGuid() };
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Validate_UserId_NoValidation()
    {
        // The validator only validates Id, not UserId
        // This test documents the current behavior - UserId has no validation rules
        var query = CreateValidQuery() with { UserId = "" }; // Empty UserId
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.UserId); // No validation on UserId

        var queryNullUserId = CreateValidQuery() with { UserId = null! }; // Null UserId
        var resultNull = _validator.TestValidate(queryNullUserId);
        resultNull.ShouldNotHaveValidationErrorFor(x => x.UserId); // No validation on UserId
    }

    [Fact]
    public void Validate_ValidQuery_ShouldNotHaveAnyErrors()
    {
        var query = CreateValidQuery();
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_OnlyIdIsValidated()
    {
        // Test that only Id field is validated (documents current behavior)
        var query = new GetOrderByIdQuery(
            Id: Guid.Empty,    // Invalid - will have error
            UserId: ""         // No validation, so no error
        );

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Id);
        result.ShouldNotHaveValidationErrorFor(x => x.UserId); // UserId not validated
    }

    private static GetOrderByIdQuery CreateValidQuery() => new(
        Id: Guid.NewGuid(),
        UserId: "user123"
    );
}