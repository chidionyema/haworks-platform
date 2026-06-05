using FluentAssertions;
using FluentValidation.TestHelper;
using Haworks.Privacy.Application.Requests.Commands.InitiateRequest;
using Haworks.Privacy.Domain.Enums;
using Xunit;

namespace Haworks.Privacy.Unit.Application.Commands;

public sealed class InitiatePrivacyRequestCommandValidatorTests
{
    private readonly InitiatePrivacyRequestCommandValidator _validator;

    public InitiatePrivacyRequestCommandValidatorTests()
    {
        _validator = new InitiatePrivacyRequestCommandValidator();
    }

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), PrivacyRequestType.Erasure);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyUserId_FailsValidation()
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.Empty, PrivacyRequestType.Erasure);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserId)
            .WithErrorMessage("'User Id' must not be empty.");
    }

    [Theory]
    [InlineData(PrivacyRequestType.Erasure)]
    [InlineData(PrivacyRequestType.Export)]
    public void Validate_ValidRequestType_PassesValidation(PrivacyRequestType requestType)
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), requestType);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Type);
    }

    [Fact]
    public void Validate_InvalidRequestType_FailsValidation()
    {
        // Arrange - cast invalid enum value
        var invalidType = (PrivacyRequestType)999;
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), invalidType);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Type)
            .WithErrorMessage("'Type' has a range of values which does not include '999'.");
    }

    [Fact]
    public void Validate_WithIdempotencyKey_PassesValidation()
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), PrivacyRequestType.Export, "idem-key-123");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyIdempotencyKey_PassesValidation()
    {
        // Arrange
        var command = new InitiatePrivacyRequestCommand(Guid.NewGuid(), PrivacyRequestType.Export, "");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}