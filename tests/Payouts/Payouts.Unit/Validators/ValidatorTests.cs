using FluentAssertions;
using FluentValidation.TestHelper;
using Haworks.Payouts.Application.Disbursements.Queries.GetPayoutsBySeller;
using Haworks.Payouts.Application.Ledger.Queries.GetBalance;
using Haworks.Payouts.Application.Sellers.Commands.GetOnboardingLink;
using Haworks.Payouts.Application.Sellers.Commands.RegisterSeller;
using Haworks.Payouts.Domain.Enums;
using Xunit;

namespace Haworks.Payouts.Unit.Validators;

public class ValidatorTests
{
    [Fact]
    public void GetPayoutsBySellerQueryValidator_Should_Require_SellerId()
    {
        // Arrange
        var validator = new GetPayoutsBySellerQueryValidator();

        // Act & Assert
        validator.TestValidate(new GetPayoutsBySellerQuery(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.SellerId);

        validator.TestValidate(new GetPayoutsBySellerQuery(Guid.NewGuid()))
            .ShouldNotHaveValidationErrorFor(x => x.SellerId);
    }

    [Fact]
    public void GetBalanceQueryValidator_Should_Require_OwnerId()
    {
        // Arrange
        var validator = new GetBalanceQueryValidator();

        // Act & Assert
        validator.TestValidate(new GetBalanceQuery(Guid.Empty, AccountType.SellerPending, "USD"))
            .ShouldHaveValidationErrorFor(x => x.OwnerId);

        validator.TestValidate(new GetBalanceQuery(Guid.NewGuid(), AccountType.SellerPending, "USD"))
            .ShouldNotHaveValidationErrorFor(x => x.OwnerId);
    }

    [Fact]
    public void RegisterSellerCommandValidator_Should_Require_SellerId()
    {
        // Arrange
        var validator = new RegisterSellerCommandValidator();

        // Act & Assert
        validator.TestValidate(new RegisterSellerCommand(Guid.Empty, "test@example.com"))
            .ShouldHaveValidationErrorFor(x => x.SellerId);

        validator.TestValidate(new RegisterSellerCommand(Guid.NewGuid(), "test@example.com"))
            .ShouldNotHaveValidationErrorFor(x => x.SellerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void RegisterSellerCommandValidator_Should_Require_Email(string invalidEmail)
    {
        // Arrange
        var validator = new RegisterSellerCommandValidator();

        // Act & Assert
        validator.TestValidate(new RegisterSellerCommand(Guid.NewGuid(), invalidEmail))
            .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("test@")]
    [InlineData("test..test@example.com")]
    public void RegisterSellerCommandValidator_Should_Require_Valid_Email_Format(string invalidEmail)
    {
        // Arrange
        var validator = new RegisterSellerCommandValidator();

        // Act & Assert
        validator.TestValidate(new RegisterSellerCommand(Guid.NewGuid(), invalidEmail))
            .ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name+tag@domain.co.uk")]
    [InlineData("user_123@test-domain.org")]
    public void RegisterSellerCommandValidator_Should_Accept_Valid_Emails(string validEmail)
    {
        // Arrange
        var validator = new RegisterSellerCommandValidator();

        // Act & Assert
        validator.TestValidate(new RegisterSellerCommand(Guid.NewGuid(), validEmail))
            .ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void GetOnboardingLinkCommandValidator_Should_Require_SellerId()
    {
        // Arrange
        var validator = new GetOnboardingLinkCommandValidator();

        // Act & Assert
        validator.TestValidate(new GetOnboardingLinkCommand(
            Guid.Empty,
            "https://example.com/return",
            "https://example.com/refresh"))
            .ShouldHaveValidationErrorFor(x => x.SellerId);

        validator.TestValidate(new GetOnboardingLinkCommand(
            Guid.NewGuid(),
            "https://example.com/return",
            "https://example.com/refresh"))
            .ShouldNotHaveValidationErrorFor(x => x.SellerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void GetOnboardingLinkCommandValidator_Should_Require_ReturnUrl(string invalidUrl)
    {
        // Arrange
        var validator = new GetOnboardingLinkCommandValidator();

        // Act & Assert
        validator.TestValidate(new GetOnboardingLinkCommand(
            Guid.NewGuid(),
            invalidUrl,
            "https://example.com/refresh"))
            .ShouldHaveValidationErrorFor(x => x.ReturnUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void GetOnboardingLinkCommandValidator_Should_Require_RefreshUrl(string invalidUrl)
    {
        // Arrange
        var validator = new GetOnboardingLinkCommandValidator();

        // Act & Assert
        validator.TestValidate(new GetOnboardingLinkCommand(
            Guid.NewGuid(),
            "https://example.com/return",
            invalidUrl))
            .ShouldHaveValidationErrorFor(x => x.RefreshUrl);
    }

    [Fact]
    public void GetOnboardingLinkCommandValidator_Should_Accept_Valid_Command()
    {
        // Arrange
        var validator = new GetOnboardingLinkCommandValidator();
        var validCommand = new GetOnboardingLinkCommand(
            Guid.NewGuid(),
            "https://example.com/return",
            "https://example.com/refresh");

        // Act & Assert
        var result = validator.TestValidate(validCommand);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetBalanceQueryValidator_Should_Accept_Valid_Query()
    {
        // Arrange
        var validator = new GetBalanceQueryValidator();
        var validQuery = new GetBalanceQuery(Guid.NewGuid(), AccountType.SellerPending, "USD");

        // Act & Assert
        var result = validator.TestValidate(validQuery);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetPayoutsBySellerQueryValidator_Should_Accept_Valid_Query()
    {
        // Arrange
        var validator = new GetPayoutsBySellerQueryValidator();
        var validQuery = new GetPayoutsBySellerQuery(Guid.NewGuid(), PageNumber: 1, PageSize: 10);

        // Act & Assert
        var result = validator.TestValidate(validQuery);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RegisterSellerCommandValidator_Should_Accept_Valid_Command()
    {
        // Arrange
        var validator = new RegisterSellerCommandValidator();
        var validCommand = new RegisterSellerCommand(Guid.NewGuid(), "seller@example.com", "idempotency-123");

        // Act & Assert
        var result = validator.TestValidate(validCommand);
        result.ShouldNotHaveAnyValidationErrors();
    }
}