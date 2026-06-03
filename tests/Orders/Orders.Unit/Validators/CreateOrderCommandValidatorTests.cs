using FluentValidation.TestHelper;
using Haworks.Orders.Application.Commands;
using Haworks.Orders.Application.Validators;
using Xunit;

namespace Haworks.Orders.Unit.Validators;

public class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    [Theory]
    [InlineData("", false)] // Empty UserId
    [InlineData(null, false)] // Null UserId
    [InlineData("valid-user", true)]
    [InlineData("user123", true)]
    [InlineData("  ", false)] // Whitespace only
    public void Validate_UserId_ReturnsExpectedResult(string? userId, bool isValid)
    {
        var command = CreateValidCommand() with { UserId = userId! };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.UserId);
        else
            result.ShouldHaveValidationErrorFor(x => x.UserId);
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
    public void Validate_CustomerEmail_ReturnsExpectedResult(string? email, bool isValid)
    {
        var command = CreateValidCommand() with { CustomerEmail = email! };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.CustomerEmail);
        else
            result.ShouldHaveValidationErrorFor(x => x.CustomerEmail);
    }

    [Theory]
    [InlineData(-1L, false)] // Negative amount
    [InlineData(0L, false)]  // Zero amount
    [InlineData(1L, true)]   // Positive amount
    [InlineData(10000L, true)] // Large amount
    public void Validate_TotalAmountCents_ReturnsExpectedResult(long amount, bool isValid)
    {
        var command = CreateValidCommand() with { TotalAmountCents = amount };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.TotalAmountCents);
        else
            result.ShouldHaveValidationErrorFor(x => x.TotalAmountCents);
    }

    [Theory]
    [InlineData("", false)]    // Empty currency
    [InlineData(null, false)]  // Null currency
    [InlineData("US", false)]  // Too short
    [InlineData("USDD", false)] // Too long
    [InlineData("USD", true)]  // Valid 3-char currency
    [InlineData("EUR", true)]  // Valid 3-char currency
    [InlineData("GBP", true)]  // Valid 3-char currency
    [InlineData("  ", false)]  // Whitespace only
    public void Validate_Currency_ReturnsExpectedResult(string? currency, bool isValid)
    {
        var command = CreateValidCommand() with { Currency = currency! };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.Currency);
        else
            result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void Validate_SagaId_Empty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { SagaId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.SagaId);
    }

    [Fact]
    public void Validate_SagaId_Valid_ShouldNotHaveError()
    {
        var command = CreateValidCommand() with { SagaId = Guid.NewGuid() };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.SagaId);
    }

    [Theory]
    [InlineData("", false)]      // Empty key
    [InlineData(null, false)]    // Null key
    [InlineData("valid-key", true)]
    [InlineData("key123", true)]
    [InlineData("  ", false)]    // Whitespace only
    public void Validate_IdempotencyKey_ReturnsExpectedResult(string? key, bool isValid)
    {
        var command = CreateValidCommand() with { IdempotencyKey = key! };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor(x => x.IdempotencyKey);
        else
            result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validate_Items_Empty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Items = [] };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_Items_Null_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Items = null! };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_Items_Valid_ShouldNotHaveError()
    {
        var command = CreateValidCommand(); // Has valid items
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_Item_ProductId_Empty_ShouldHaveError()
    {
        var command = CreateValidCommand() with
        {
            Items = [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.Empty)]
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Items[0].ProductId");
    }

    [Theory]
    [InlineData("", false)]      // Empty product name
    [InlineData(null, false)]    // Null product name
    [InlineData("Product", true)]
    [InlineData("  ", false)]    // Whitespace only
    public void Validate_Item_ProductName_ReturnsExpectedResult(string? productName, bool isValid)
    {
        var command = CreateValidCommand() with
        {
            Items = [new CreateOrderCommand.OrderItemCommand(productName!, 1, 1000L, Guid.NewGuid())]
        };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor("Items[0].ProductName");
        else
            result.ShouldHaveValidationErrorFor("Items[0].ProductName");
    }

    [Theory]
    [InlineData(-1, false)]  // Negative quantity
    [InlineData(0, false)]   // Zero quantity
    [InlineData(1, true)]    // Valid quantity
    [InlineData(100, true)]  // Large quantity
    public void Validate_Item_Quantity_ReturnsExpectedResult(int quantity, bool isValid)
    {
        var command = CreateValidCommand() with
        {
            Items = [new CreateOrderCommand.OrderItemCommand("Product", quantity, 1000L, Guid.NewGuid())]
        };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor("Items[0].Quantity");
        else
            result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Theory]
    [InlineData(-1L, false)] // Negative price
    [InlineData(0L, false)]  // Zero price
    [InlineData(1L, true)]   // Valid price
    [InlineData(10000L, true)] // Large price
    public void Validate_Item_UnitPriceCents_ReturnsExpectedResult(long unitPrice, bool isValid)
    {
        var command = CreateValidCommand() with
        {
            Items = [new CreateOrderCommand.OrderItemCommand("Product", 1, unitPrice, Guid.NewGuid())]
        };
        var result = _validator.TestValidate(command);

        if (isValid)
            result.ShouldNotHaveValidationErrorFor("Items[0].UnitPriceCents");
        else
            result.ShouldHaveValidationErrorFor("Items[0].UnitPriceCents");
    }

    [Fact]
    public void Validate_MultipleItems_AllValid_ShouldNotHaveErrors()
    {
        var command = CreateValidCommand() with
        {
            Items = [
                new CreateOrderCommand.OrderItemCommand("Product1", 1, 1000L, Guid.NewGuid()),
                new CreateOrderCommand.OrderItemCommand("Product2", 2, 2000L, Guid.NewGuid()),
                new CreateOrderCommand.OrderItemCommand("Product3", 3, 3000L, Guid.NewGuid())
            ]
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MultipleItems_SomeInvalid_ShouldHaveCorrectErrors()
    {
        var command = CreateValidCommand() with
        {
            Items = [
                new CreateOrderCommand.OrderItemCommand("Product1", 1, 1000L, Guid.NewGuid()), // Valid
                new CreateOrderCommand.OrderItemCommand("", 0, -100L, Guid.Empty),              // All invalid
                new CreateOrderCommand.OrderItemCommand("Product3", 3, 3000L, Guid.NewGuid())  // Valid
            ]
        };
        var result = _validator.TestValidate(command);

        // Should not have errors for valid items
        result.ShouldNotHaveValidationErrorFor("Items[0].ProductName");
        result.ShouldNotHaveValidationErrorFor("Items[2].ProductName");

        // Should have errors for invalid item
        result.ShouldHaveValidationErrorFor("Items[1].ProductName");
        result.ShouldHaveValidationErrorFor("Items[1].Quantity");
        result.ShouldHaveValidationErrorFor("Items[1].UnitPriceCents");
        result.ShouldHaveValidationErrorFor("Items[1].ProductId");
    }

    private static CreateOrderCommand CreateValidCommand() => new(
        UserId: "user123",
        CustomerEmail: "test@example.com",
        TotalAmountCents: 1000L,
        Currency: "USD",
        SagaId: Guid.NewGuid(),
        IdempotencyKey: "key123",
        Items: [new CreateOrderCommand.OrderItemCommand("Product", 1, 1000L, Guid.NewGuid())]
    );
}