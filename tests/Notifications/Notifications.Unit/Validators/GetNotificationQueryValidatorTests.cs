using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;
using Haworks.Notifications.Application.Queries;

namespace Haworks.Notifications.Unit.Validators;

[Trait("Category", "Unit")]
public sealed class GetNotificationQueryValidatorTests
{
    private readonly GetNotificationQueryValidator _validator = new();

    [Fact]
    public void Validate_ValidId_PassesValidation()
    {
        // Arrange
        var query = new GetNotificationQuery(Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidIdWithRequestingUserId_PassesValidation()
    {
        // Arrange
        var query = new GetNotificationQuery(Guid.NewGuid(), RequestingUserId: "user-123");

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyId_FailsValidation()
    {
        // Arrange
        var query = new GetNotificationQuery(Guid.Empty);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(q => q.Id);
    }

    [Fact]
    public void Validate_NullRequestingUserId_PassesValidation()
    {
        // Arrange
        var query = new GetNotificationQuery(Guid.NewGuid(), RequestingUserId: null);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyStringRequestingUserId_PassesValidation()
    {
        // Arrange - empty string is allowed for RequestingUserId
        var query = new GetNotificationQuery(Guid.NewGuid(), RequestingUserId: "");

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhitespaceRequestingUserId_PassesValidation()
    {
        // Arrange - whitespace is allowed for RequestingUserId
        var query = new GetNotificationQuery(Guid.NewGuid(), RequestingUserId: "   ");

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}