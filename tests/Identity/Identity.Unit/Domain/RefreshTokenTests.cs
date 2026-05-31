using Haworks.Identity.Domain;
using Haworks.BuildingBlocks.Persistence;
using Xunit;

namespace Haworks.Identity.Unit.Domain;

public class RefreshTokenTests
{
    [Fact]
    public void Create_ValidParameters_CreatesRefreshToken()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);

        // Act
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Assert
        Assert.Equal(userId, refreshToken.UserId);
        Assert.Equal(token, refreshToken.Token);
        Assert.Equal(expires, refreshToken.Expires);
        Assert.Null(refreshToken.User);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_InvalidUserId_ThrowsArgumentException(string invalidUserId)
    {
        // Arrange
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(invalidUserId, token, expires));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_InvalidToken_ThrowsArgumentException(string invalidToken)
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var expires = DateTime.UtcNow.AddDays(7);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RefreshToken.Create(userId, invalidToken, expires));
    }

    [Fact]
    public void IsExpired_TokenNotExpired_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7); // Future date
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        var isExpired = refreshToken.IsExpired;

        // Assert
        Assert.False(isExpired);
    }

    [Fact]
    public void IsExpired_TokenExpired_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "expired-refresh-token";
        var expires = DateTime.UtcNow.AddDays(-1); // Past date
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        var isExpired = refreshToken.IsExpired;

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public void IsExpired_TokenExpiresNow_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "expiring-now-token";
        var expires = DateTime.UtcNow; // Expires right now
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        var isExpired = refreshToken.IsExpired;

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public void IsExpired_TokenExpiresInOneSecond_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "almost-expiring-token";
        var expires = DateTime.UtcNow.AddSeconds(1); // Expires in 1 second
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        var isExpired = refreshToken.IsExpired;

        // Assert
        Assert.False(isExpired);
    }

    [Fact]
    public void SetUser_ValidUser_SetsUserReference()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user = new User { Id = userId, UserName = "testuser", Email = "test@example.com" };
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        refreshToken.SetUser(user);

        // Assert
        Assert.Equal(user, refreshToken.User);
    }

    [Fact]
    public void SetUser_NullUser_ThrowsArgumentNullException()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => refreshToken.SetUser(null!));
    }

    [Fact]
    public void SetUser_DifferentUserId_StillSetsUser()
    {
        // Arrange
        var userId1 = Guid.NewGuid().ToString();
        var userId2 = Guid.NewGuid().ToString();
        var user = new User { Id = userId2, UserName = "testuser", Email = "test@example.com" };
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);
        var refreshToken = RefreshToken.Create(userId1, token, expires);

        // Act
        refreshToken.SetUser(user);

        // Assert
        Assert.Equal(user, refreshToken.User);
        Assert.Equal(userId1, refreshToken.UserId); // Original UserId should remain unchanged
    }

    [Fact]
    public void SetUser_OverwriteExistingUser_SetsNewUser()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var user1 = new User { Id = userId, UserName = "testuser1", Email = "test1@example.com" };
        var user2 = new User { Id = userId, UserName = "testuser2", Email = "test2@example.com" };
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act
        refreshToken.SetUser(user1);
        refreshToken.SetUser(user2);

        // Assert
        Assert.Equal(user2, refreshToken.User);
        Assert.NotEqual(user1, refreshToken.User);
    }

    [Fact]
    public void Create_WithMinDateTime_HandlesEdgeCase()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "edge-case-token";
        var expires = DateTime.MinValue;

        // Act
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Assert
        Assert.Equal(expires, refreshToken.Expires);
        Assert.True(refreshToken.IsExpired); // MinValue is definitely expired
    }

    [Fact]
    public void Create_WithMaxDateTime_HandlesEdgeCase()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "edge-case-token";
        var expires = DateTime.MaxValue;

        // Act
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Assert
        Assert.Equal(expires, refreshToken.Expires);
        Assert.False(refreshToken.IsExpired); // MaxValue is definitely not expired
    }

    [Fact]
    public void Create_InheritsFromAuditableEntity()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "valid-refresh-token";
        var expires = DateTime.UtcNow.AddDays(7);

        // Act
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Assert
        Assert.IsAssignableFrom<AuditableEntity>(refreshToken);
        Assert.True(refreshToken.CreatedAt <= DateTime.UtcNow);
        Assert.True(refreshToken.CreatedAt >= DateTime.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public void Properties_AreImmutableAfterCreation()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var token = "immutable-token";
        var expires = DateTime.UtcNow.AddDays(7);
        var refreshToken = RefreshToken.Create(userId, token, expires);

        // Act - Properties should have private setters
        var userIdProperty = typeof(RefreshToken).GetProperty(nameof(RefreshToken.UserId));
        var tokenProperty = typeof(RefreshToken).GetProperty(nameof(RefreshToken.Token));
        var expiresProperty = typeof(RefreshToken).GetProperty(nameof(RefreshToken.Expires));

        // Assert - Properties should not have public setters
        Assert.True(userIdProperty?.SetMethod?.IsPrivate);
        Assert.True(tokenProperty?.SetMethod?.IsPrivate);
        Assert.True(expiresProperty?.SetMethod?.IsPrivate);
    }
}