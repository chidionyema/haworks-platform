using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;
using Haworks.Notifications.Application.Webhooks;

namespace Haworks.Notifications.Unit.Validators;

[Trait("Category", "Unit")]
public sealed class UpdateNotificationStatusFromWebhookCommandValidatorTests
{
    private readonly UpdateNotificationStatusFromWebhookCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_PassesAllRules()
    {
        // Arrange
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "sg-msg-123",
            EventType: "delivered",
            RawPayload: """{"event": "delivered", "message_id": "sg-msg-123"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyProvider_ReportsError(string? provider)
    {
        // Arrange
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: provider!,
            ProviderMessageId: "msg-123",
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Provider);
    }

    [Fact]
    public void Validate_ProviderTooLong_ReportsError()
    {
        // Arrange - exceeds 100 character limit
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: new string('a', 101),
            ProviderMessageId: "msg-123",
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Provider);
    }

    [Fact]
    public void Validate_ProviderAtMaxLength_PassesValidation()
    {
        // Arrange - exactly 100 characters
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: new string('a', 100),
            ProviderMessageId: "msg-123",
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.Provider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyProviderMessageId_ReportsError(string? providerMessageId)
    {
        // Arrange
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: providerMessageId!,
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.ProviderMessageId);
    }

    [Fact]
    public void Validate_ProviderMessageIdTooLong_ReportsError()
    {
        // Arrange - exceeds 500 character limit
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: new string('a', 501),
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.ProviderMessageId);
    }

    [Fact]
    public void Validate_ProviderMessageIdAtMaxLength_PassesValidation()
    {
        // Arrange - exactly 500 characters
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: new string('a', 500),
            EventType: "delivered",
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.ProviderMessageId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyEventType_ReportsError(string? eventType)
    {
        // Arrange
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "msg-123",
            EventType: eventType!,
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.EventType);
    }

    [Fact]
    public void Validate_EventTypeTooLong_ReportsError()
    {
        // Arrange - exceeds 100 character limit
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "msg-123",
            EventType: new string('a', 101),
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.EventType);
    }

    [Fact]
    public void Validate_EventTypeAtMaxLength_PassesValidation()
    {
        // Arrange - exactly 100 characters
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "msg-123",
            EventType: new string('a', 100),
            RawPayload: """{"event": "delivered"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.EventType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyRawPayload_ReportsError(string? rawPayload)
    {
        // Arrange
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "msg-123",
            EventType: "delivered",
            RawPayload: rawPayload!);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.RawPayload);
    }

    [Fact]
    public void Validate_LargeRawPayload_PassesValidation()
    {
        // Arrange - large payload should be allowed (no max length on RawPayload)
        var largePayload = """{"event": "delivered", "data": """ + new string('"', 5000) + new string('a', 5000) + new string('"', 1) + "}";
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: "sendgrid",
            ProviderMessageId: "msg-123",
            EventType: "delivered",
            RawPayload: largePayload);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(c => c.RawPayload);
    }

    [Fact]
    public void Validate_AllFieldsAtBoundaries_PassesValidation()
    {
        // Arrange - all fields at their max allowed values
        var command = new UpdateNotificationStatusFromWebhookCommand(
            Provider: new string('p', 100),
            ProviderMessageId: new string('m', 500),
            EventType: new string('e', 100),
            RawPayload: """{"valid": "json"}""");

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_CommonWebhookProviders_PassesValidation()
    {
        // Arrange & Act & Assert
        var providers = new[] { "sendgrid", "ses", "twilio", "fcm", "webhook" };

        foreach (var provider in providers)
        {
            var command = new UpdateNotificationStatusFromWebhookCommand(
                Provider: provider,
                ProviderMessageId: "msg-123",
                EventType: "delivered",
                RawPayload: """{"event": "delivered"}""");

            var result = _validator.TestValidate(command);
            result.ShouldNotHaveAnyValidationErrors();
        }
    }
}