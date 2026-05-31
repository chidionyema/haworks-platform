using FluentAssertions;
using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Application.Disbursements.Queries.GetPayoutsBySeller;
using Haworks.Payouts.Application.Ledger.Queries.GetBalance;
using Haworks.Payouts.Application.Sellers.Commands.GetOnboardingLink;
using Haworks.Payouts.Application.Sellers.Commands.RegisterSeller;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Haworks.Payouts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace Haworks.Payouts.Unit.Application;

public sealed class HandlerTests : IDisposable
{
    private readonly PayoutsDbContext _context;
    private readonly Mock<IPayoutGateway> _mockGateway;

    public HandlerTests()
    {
        var options = new DbContextOptionsBuilder<PayoutsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new PayoutsDbContext(options);
        _mockGateway = new Mock<IPayoutGateway>();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetBalanceQueryHandler_Should_Return_Account_Balance()
    {
        // Arrange
        var handler = new GetBalanceQueryHandler(_context, NullLogger<GetBalanceQueryHandler>.Instance);
        var ownerId = Guid.NewGuid();
        var currency = "USD";
        var accountType = AccountType.SellerPending;
        var balanceCents = 15000L;

        var account = LedgerAccount.Create(ownerId, accountType, currency);
        account.UpdateBalance(balanceCents, EntryType.Credit);
        _context.LedgerAccounts.Add(account);
        await _context.SaveChangesAsync();

        var query = new GetBalanceQuery(ownerId, accountType, currency);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(balanceCents);
    }

    [Fact]
    public async Task GetBalanceQueryHandler_Should_Return_Zero_For_Nonexistent_Account()
    {
        // Arrange
        var handler = new GetBalanceQueryHandler(_context, NullLogger<GetBalanceQueryHandler>.Instance);
        var query = new GetBalanceQuery(Guid.NewGuid(), AccountType.SellerPending, "USD");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().Be(0L);
    }

    [Fact]
    public async Task GetPayoutsBySellerQueryHandler_Should_Return_Paginated_Results()
    {
        // Arrange
        var handler = new GetPayoutsBySellerQueryHandler(_context, NullLogger<GetPayoutsBySellerQueryHandler>.Instance);
        var sellerId = Guid.NewGuid();

        // Create test payouts
        for (int i = 0; i < 5; i++)
        {
            var payout = Payout.Create(sellerId, 1000L * (i + 1), "USD", "test-external-id-" + i);
            _context.Payouts.Add(payout);
        }
        await _context.SaveChangesAsync();

        var query = new GetPayoutsBySellerQuery(sellerId, PageNumber: 1, PageSize: 3);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(3);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetPayoutsBySellerQueryHandler_Should_Return_Empty_For_Nonexistent_Seller()
    {
        // Arrange
        var handler = new GetPayoutsBySellerQueryHandler(_context, NullLogger<GetPayoutsBySellerQueryHandler>.Instance);
        var query = new GetPayoutsBySellerQuery(Guid.NewGuid(), PageNumber: 1, PageSize: 10);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task RegisterSellerCommandHandler_Should_Create_New_Seller()
    {
        // Arrange
        var handler = new RegisterSellerCommandHandler(_context, _mockGateway.Object, NullLogger<RegisterSellerCommandHandler>.Instance);
        var sellerId = Guid.NewGuid();
        var email = "test@example.com";
        var externalId = "acct_test123";

        _mockGateway.Setup(x => x.CreateConnectedAccountAsync(sellerId, email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalId);

        var command = new RegisterSellerCommand(sellerId, email);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();

        var profile = await _context.SellerProfiles.FirstAsync(p => p.Id == result);
        profile.SellerId.Should().Be(sellerId);
        profile.ExternalProviderId.Should().Be(externalId);

        _mockGateway.Verify(x => x.CreateConnectedAccountAsync(sellerId, email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterSellerCommandHandler_Should_Return_Existing_Seller_Id()
    {
        // Arrange
        var handler = new RegisterSellerCommandHandler(_context, _mockGateway.Object, NullLogger<RegisterSellerCommandHandler>.Instance);
        var sellerId = Guid.NewGuid();

        var existingProfile = SellerProfile.Create(sellerId);
        _context.SellerProfiles.Add(existingProfile);
        await _context.SaveChangesAsync();

        var command = new RegisterSellerCommand(sellerId, "test@example.com");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(existingProfile.Id);
        _mockGateway.Verify(x => x.CreateConnectedAccountAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOnboardingLinkCommandHandler_Should_Create_Onboarding_Link()
    {
        // Arrange
        var handler = new GetOnboardingLinkCommandHandler(_context, _mockGateway.Object);
        var sellerId = Guid.NewGuid();
        var validReturnUrl = "https://example.com/onboarding/complete";
        var validRefreshUrl = "https://example.com/onboarding/refresh";
        var expectedOnboardingLink = "https://connect.stripe.com/oauth/authorize?client_id=test";

        // Create seller profile
        var profile = SellerProfile.Create(sellerId);
        profile.ExternalProviderId = "acct_test123";
        _context.SellerProfiles.Add(profile);
        await _context.SaveChangesAsync();

        _mockGateway.Setup(x => x.CreateAccountOnboardingLinkAsync("acct_test123", validReturnUrl, validRefreshUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedOnboardingLink);

        var command = new GetOnboardingLinkCommand(sellerId, validReturnUrl, validRefreshUrl);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(expectedOnboardingLink);
        _mockGateway.Verify(x => x.CreateAccountOnboardingLinkAsync("acct_test123", validReturnUrl, validRefreshUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("javascript:alert('xss')", "Invalid URL")]
    [InlineData("http://example.com/insecure", "URL must use HTTPS")]
    [InlineData("https://localhost/danger", "URL must not point to internal hosts")]
    [InlineData("https://127.0.0.1/danger", "URL must not point to internal hosts")]
    [InlineData("not-a-url", "Invalid URL")]
    public async Task GetOnboardingLinkCommandHandler_Should_Reject_Invalid_URLs(string invalidUrl, string expectedErrorMessage)
    {
        // Arrange
        var handler = new GetOnboardingLinkCommandHandler(_context, _mockGateway.Object);
        var command = new GetOnboardingLinkCommand(Guid.NewGuid(), invalidUrl, "https://example.com/valid");

        // Act & Assert
        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedErrorMessage}*");

        _mockGateway.Verify(x => x.CreateAccountOnboardingLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}