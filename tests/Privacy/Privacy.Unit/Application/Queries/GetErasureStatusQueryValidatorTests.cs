using FluentAssertions;
using FluentValidation.TestHelper;
using Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;
using Xunit;

namespace Haworks.Privacy.Unit.Application.Queries;

public sealed class GetErasureStatusQueryValidatorTests
{
    private readonly GetErasureStatusQueryValidator _validator;

    public GetErasureStatusQueryValidatorTests()
    {
        _validator = new GetErasureStatusQueryValidator();
    }

    [Fact]
    public void Validate_ValidQuery_PassesValidation()
    {
        // Arrange
        var query = new GetErasureStatusQuery(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyRequestId_FailsValidation()
    {
        // Arrange
        var query = new GetErasureStatusQuery(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.RequestId)
            .WithErrorMessage("'Request Id' must not be empty.");
    }

    [Fact]
    public void Validate_EmptyUserId_FailsValidation()
    {
        // Arrange
        var query = new GetErasureStatusQuery(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserId)
            .WithErrorMessage("'User Id' must not be empty.");
    }

    [Fact]
    public void Validate_BothIdsEmpty_FailsBothValidations()
    {
        // Arrange
        var query = new GetErasureStatusQuery(Guid.Empty, Guid.Empty);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.RequestId);
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}