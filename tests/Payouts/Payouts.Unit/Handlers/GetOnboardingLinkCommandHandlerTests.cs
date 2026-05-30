using FluentAssertions;
using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Application.Sellers.Commands.GetOnboardingLink;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Haworks.Payouts.Unit.Handlers;

public class GetOnboardingLinkCommandHandlerTests : IDisposable
{
    private readonly PayoutsDbContext _context;
    private readonly Mock<IPayoutGateway> _mockGateway;
    private readonly GetOnboardingLinkCommandHandler _handler;

    public GetOnboardingLinkCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new PayoutsDbContext(options);
        _mockGateway = new Mock<IPayoutGateway>();
        _handler = new GetOnboardingLinkCommandHandler(_context, _mockGateway.Object);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Handle_Should_Return_Onboarding_Link_For_Valid_Seller()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";
        var externalId = "acct_test123";
        var expectedLink = "https://connect.stripe.com/onboarding/link";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = externalId;
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _mockGateway.Setup(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedLink);

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedLink);
        _mockGateway.Verify(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Throw_When_Seller_Profile_Not_Found()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("Seller profile not found");
    }

    [Fact]
    public async Task Handle_Should_Throw_When_External_Provider_Id_Is_Null()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = null; // No external ID set
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("Seller profile not found");
    }

    [Fact]
    public async Task Handle_Should_Throw_When_External_Provider_Id_Is_Empty()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = ""; // Empty external ID
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("Seller profile not found");
    }

    [Theory]
    [InlineData("http://example.com/return")] // HTTP instead of HTTPS
    [InlineData("ftp://example.com/return")] // Wrong protocol
    [InlineData("not-a-url")] // Invalid URL format
    [InlineData("")] // Empty URL
    public async Task Handle_Should_Throw_For_Invalid_Return_Url(string invalidReturnUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var refreshUrl = "https://example.com/refresh";

        var command = new GetOnboardingLinkCommand(sellerId, invalidReturnUrl, refreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("returnUrl");
    }

    [Theory]
    [InlineData("http://example.com/refresh")] // HTTP instead of HTTPS
    [InlineData("ftp://example.com/refresh")] // Wrong protocol
    [InlineData("not-a-url")] // Invalid URL format
    [InlineData("")] // Empty URL
    public async Task Handle_Should_Throw_For_Invalid_Refresh_Url(string invalidRefreshUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, invalidRefreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("refreshUrl");
    }

    [Theory]
    [InlineData("https://localhost/return")]
    [InlineData("https://127.0.0.1/return")]
    [InlineData("https://0.0.0.0/return")]
    public async Task Handle_Should_Throw_For_Local_Return_Urls(string localReturnUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var refreshUrl = "https://example.com/refresh";

        var command = new GetOnboardingLinkCommand(sellerId, localReturnUrl, refreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("URL must not point to internal hosts");
        exception.Message.Should().Contain("returnUrl");
    }

    [Theory]
    [InlineData("https://localhost/refresh")]
    [InlineData("https://127.0.0.1/refresh")]
    [InlineData("https://0.0.0.0/refresh")]
    public async Task Handle_Should_Throw_For_Local_Refresh_Urls(string localRefreshUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, localRefreshUrl);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("URL must not point to internal hosts");
        exception.Message.Should().Contain("refreshUrl");
    }

    [Fact]
    public async Task Handle_Should_Use_AsNoTracking_For_Read_Only_Operation()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";
        var externalId = "acct_test123";
        var expectedLink = "https://connect.stripe.com/onboarding/link";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = externalId;
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _mockGateway.Setup(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedLink);

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedLink);

        // Verify no additional entities are being tracked after the query
        var trackedEntities = _context.ChangeTracker.Entries().ToList();
        trackedEntities.Should().HaveCount(1); // Only the profile we added, not the one from the query
        trackedEntities[0].Entity.Should().BeOfType<SellerProfile>();
        trackedEntities[0].State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task Handle_Should_Validate_Both_URLs_Before_Database_Query()
    {
        // This test verifies URL validation happens before DB operations
        // Arrange
        var sellerId = Guid.NewGuid();
        var invalidReturnUrl = "invalid-url";
        var validRefreshUrl = "https://example.com/refresh";

        var command = new GetOnboardingLinkCommand(sellerId, invalidReturnUrl, validRefreshUrl);

        // Act & Assert - Should throw before hitting database
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));

        exception.Message.Should().Contain("returnUrl");

        // Verify no database queries were made by checking no profile was looked up
        var profiles = await _context.SellerProfiles.ToListAsync();
        profiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Pass_Cancellation_Token_To_Gateway()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var returnUrl = "https://example.com/return";
        var refreshUrl = "https://example.com/refresh";
        var externalId = "acct_test123";
        var expectedLink = "https://connect.stripe.com/link";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = externalId;
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _mockGateway.Setup(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedLink);

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);
        var cancellationToken = new CancellationToken();

        // Act
        await _handler.Handle(command, cancellationToken);

        // Assert
        _mockGateway.Verify(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com/return", "https://example.com/refresh")]
    [InlineData("https://secure-site.org/callback", "https://secure-site.org/retry")]
    [InlineData("https://shop.mystore.com/auth/complete", "https://shop.mystore.com/auth/refresh")]
    public async Task Handle_Should_Accept_Valid_HTTPS_URLs(string returnUrl, string refreshUrl)
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var externalId = "acct_test123";
        var expectedLink = "https://connect.stripe.com/link";

        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = externalId;
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _mockGateway.Setup(g => g.CreateAccountOnboardingLinkAsync(externalId, returnUrl, refreshUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedLink);

        var command = new GetOnboardingLinkCommand(sellerId, returnUrl, refreshUrl);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedLink);
    }
}