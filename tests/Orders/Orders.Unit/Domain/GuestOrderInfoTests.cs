using FluentAssertions;
using Haworks.Orders.Domain;
using Xunit;

namespace Haworks.Orders.Unit.Domain;

public class GuestOrderInfoTests
{
    [Fact]
    public void Create_WithValidData_ReturnsCompletedGuestOrderInfo()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var email = "guest@example.com";
        var firstName = "John";
        var lastName = "Doe";
        var address = "123 Main St";
        var city = "Anytown";
        var state = "CA";
        var postalCode = "12345";
        var country = "US";
        var phone = "+1-555-123-4567";
        var orderToken = "guest-token-123";

        // Act
        var guestOrderInfo = GuestOrderInfo.Create(
            orderId, email, firstName, lastName,
            address, city, state, postalCode, country,
            phone, orderToken);

        // Assert
        guestOrderInfo.Should().NotBeNull();
        guestOrderInfo.Id.Should().NotBe(Guid.Empty);
        guestOrderInfo.OrderId.Should().Be(orderId);
        guestOrderInfo.Email.Should().Be(email);
        guestOrderInfo.FirstName.Should().Be(firstName);
        guestOrderInfo.LastName.Should().Be(lastName);
        guestOrderInfo.Address.Should().Be(address);
        guestOrderInfo.City.Should().Be(city);
        guestOrderInfo.State.Should().Be(state);
        guestOrderInfo.PostalCode.Should().Be(postalCode);
        guestOrderInfo.Country.Should().Be(country);
        guestOrderInfo.Phone.Should().Be(phone);
        guestOrderInfo.OrderToken.Should().Be(orderToken);
        guestOrderInfo.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Create_WithNullPhone_ReturnsValidGuestOrderInfo()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderToken = "guest-token-123";

        // Act
        var guestOrderInfo = GuestOrderInfo.Create(
            orderId, "guest@example.com", "John", "Doe",
            "123 Main St", "Anytown", "CA", "12345", "US",
            null, orderToken);

        // Assert
        guestOrderInfo.Phone.Should().BeNull();
        guestOrderInfo.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "John", "Doe", "token")]           // Empty email
    [InlineData(null, "John", "Doe", "token")]         // Null email
    [InlineData("  ", "John", "Doe", "token")]         // Whitespace email
    [InlineData("guest@example.com", "", "Doe", "token")]      // Empty firstName
    [InlineData("guest@example.com", null, "Doe", "token")]    // Null firstName
    [InlineData("guest@example.com", "  ", "Doe", "token")]    // Whitespace firstName
    [InlineData("guest@example.com", "John", "", "token")]     // Empty lastName
    [InlineData("guest@example.com", "John", null, "token")]   // Null lastName
    [InlineData("guest@example.com", "John", "  ", "token")]   // Whitespace lastName
    [InlineData("guest@example.com", "John", "Doe", "")]       // Empty token
    [InlineData("guest@example.com", "John", "Doe", null)]     // Null token
    [InlineData("guest@example.com", "John", "Doe", "  ")]     // Whitespace token
    public void Create_WithInvalidRequiredFields_ThrowsArgumentException(
        string? email, string? firstName, string? lastName, string? orderToken)
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act & Assert
        var act = () => GuestOrderInfo.Create(
            orderId, email!, firstName!, lastName!,
            "123 Main St", "Anytown", "CA", "12345", "US",
            null, orderToken!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateStub_WithValidData_ReturnsIncompleteGuestOrderInfo()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderToken = "stub-token-456";

        // Act
        var guestOrderInfo = GuestOrderInfo.CreateStub(orderId, orderToken);

        // Assert
        guestOrderInfo.Should().NotBeNull();
        guestOrderInfo.Id.Should().NotBe(Guid.Empty);
        guestOrderInfo.OrderId.Should().Be(orderId);
        guestOrderInfo.OrderToken.Should().Be(orderToken);
        guestOrderInfo.IsComplete.Should().BeFalse();

        // All other fields should be null for stub
        guestOrderInfo.Email.Should().BeNull();
        guestOrderInfo.FirstName.Should().BeNull();
        guestOrderInfo.LastName.Should().BeNull();
        guestOrderInfo.Address.Should().BeNull();
        guestOrderInfo.City.Should().BeNull();
        guestOrderInfo.State.Should().BeNull();
        guestOrderInfo.PostalCode.Should().BeNull();
        guestOrderInfo.Country.Should().BeNull();
        guestOrderInfo.Phone.Should().BeNull();
    }

    [Theory]
    [InlineData("")]       // Empty token
    [InlineData(null)]     // Null token
    [InlineData("  ")]     // Whitespace token
    public void CreateStub_WithInvalidToken_ThrowsArgumentException(string? orderToken)
    {
        // Arrange
        var orderId = Guid.NewGuid();

        // Act & Assert
        var act = () => GuestOrderInfo.CreateStub(orderId, orderToken!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Complete_WithValidData_UpdatesGuestOrderInfoToComplete()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderToken = "stub-token-789";
        var guestOrderInfo = GuestOrderInfo.CreateStub(orderId, orderToken);

        var email = "completed@example.com";
        var firstName = "Jane";
        var lastName = "Smith";
        var address = "456 Oak Ave";
        var city = "Somewhere";
        var state = "NY";
        var postalCode = "67890";
        var country = "US";
        var phone = "+1-555-987-6543";

        // Act
        guestOrderInfo.Complete(email, firstName, lastName, address, city, state, postalCode, country, phone);

        // Assert
        guestOrderInfo.IsComplete.Should().BeTrue();
        guestOrderInfo.Email.Should().Be(email);
        guestOrderInfo.FirstName.Should().Be(firstName);
        guestOrderInfo.LastName.Should().Be(lastName);
        guestOrderInfo.Address.Should().Be(address);
        guestOrderInfo.City.Should().Be(city);
        guestOrderInfo.State.Should().Be(state);
        guestOrderInfo.PostalCode.Should().Be(postalCode);
        guestOrderInfo.Country.Should().Be(country);
        guestOrderInfo.Phone.Should().Be(phone);

        // Original properties should remain unchanged
        guestOrderInfo.OrderId.Should().Be(orderId);
        guestOrderInfo.OrderToken.Should().Be(orderToken);
    }

    [Fact]
    public void Complete_WithNullPhone_UpdatesGuestOrderInfo()
    {
        // Arrange
        var guestOrderInfo = GuestOrderInfo.CreateStub(Guid.NewGuid(), "token");

        // Act
        guestOrderInfo.Complete(
            "test@example.com", "First", "Last",
            "Address", "City", "State", "12345", "US", null);

        // Assert
        guestOrderInfo.IsComplete.Should().BeTrue();
        guestOrderInfo.Phone.Should().BeNull();
    }

    [Fact]
    public void Complete_OnAlreadyCompleteInfo_UpdatesAllFields()
    {
        // Arrange - create already complete guest order info
        var orderId = Guid.NewGuid();
        var orderToken = "complete-token";
        var originalInfo = GuestOrderInfo.Create(
            orderId, "original@example.com", "Original", "Name",
            "Original Address", "Original City", "OS", "00000", "US",
            "+1-555-000-0000", orderToken);

        // Act - complete with new data
        originalInfo.Complete(
            "updated@example.com", "Updated", "Name",
            "Updated Address", "Updated City", "US", "11111", "US",
            "+1-555-111-1111");

        // Assert - all fields should be updated
        originalInfo.Email.Should().Be("updated@example.com");
        originalInfo.FirstName.Should().Be("Updated");
        originalInfo.LastName.Should().Be("Name");
        originalInfo.Address.Should().Be("Updated Address");
        originalInfo.City.Should().Be("Updated City");
        originalInfo.State.Should().Be("US");
        originalInfo.PostalCode.Should().Be("11111");
        originalInfo.Country.Should().Be("US");
        originalInfo.Phone.Should().Be("+1-555-111-1111");
        originalInfo.IsComplete.Should().BeTrue();

        // Immutable properties should remain unchanged
        originalInfo.OrderId.Should().Be(orderId);
        originalInfo.OrderToken.Should().Be(orderToken);
    }
}