namespace Haworks.Identity.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials = new("Auth.InvalidCredentials", "Invalid username or password", ErrorType.Unauthorized);
    public static readonly Error TokenExpired = new("Auth.TokenExpired", "Token has expired", ErrorType.Unauthorized);
    public static readonly Error TokenRevoked = new("Auth.TokenRevoked", "Token has been revoked", ErrorType.Unauthorized);
    public static readonly Error Unauthorized = new("Auth.Unauthorized", "User is not authorized", ErrorType.Unauthorized);
    public static readonly Error UserNotFound = new("Auth.UserNotFound", "User not found", ErrorType.Unauthorized);
    public static readonly Error MissingTokens = new("Auth.MissingTokens", "Access and refresh tokens are required.", ErrorType.Validation);
    public static readonly Error InvalidAccessToken = new("Auth.InvalidAccessToken", "Invalid access token structure.", ErrorType.Unauthorized);
    public static readonly Error MissingUserId = new("Auth.MissingUserId", "Token claims are missing user identification.", ErrorType.Unauthorized);
    public static readonly Error InvalidRefreshToken = new("Auth.InvalidRefreshToken", "Session invalid. Please log in again.", ErrorType.Unauthorized);
    public static readonly Error TokenProcessingError = new("Auth.TokenProcessingError", "Invalid token signature.", ErrorType.Unauthorized);
    public static readonly Error RefreshFailed = new("Auth.RefreshFailed", "An internal error occurred.", ErrorType.Internal);
    // Surfaced as 400 (not 404) so callers attempting to unlink a provider
    // they don't have a login for get a clear bad-request response rather
    // than an ambiguous resource-not-found.
    public static readonly Error LoginNotFound = new("Auth.LoginNotFound", "External login not found.", ErrorType.Validation);
    public static readonly Error LinkFailed = new("Auth.LinkFailed", "Unable to link external login.", ErrorType.Internal);
    public static readonly Error UnlinkFailed = new("Auth.UnlinkFailed", "Unable to unlink external login.", ErrorType.Internal);
    public static readonly Error RegistrationFailed = new("Auth.RegistrationFailed", "Registration failed.", ErrorType.Validation);
    public static readonly Error RoleAssignmentFailed = new("Auth.RoleAssignmentFailed", "Role assignment failed.", ErrorType.Internal);
    public static readonly Error ClaimAssignmentFailed = new("Auth.ClaimAssignmentFailed", "Claim assignment failed.", ErrorType.Internal);
    public static readonly Error InvalidProviderKey = new("Auth.InvalidProviderKey", "External login information is incomplete.", ErrorType.Validation);
    public static readonly Error InvalidContext = new("Auth.InvalidContext", "HTTP context is required.", ErrorType.Validation);
    public static readonly Error ExternalLoginFailed = new("Auth.ExternalLoginFailed", "Unable to retrieve external login information.", ErrorType.Validation);
    public static readonly Error MissingEmail = new("Auth.MissingEmail", "Email is required but not provided by the external login.", ErrorType.Validation);
    public static readonly Error UnverifiedEmail = new("Auth.UnverifiedEmail", "Email must be verified by the external provider before linking accounts.", ErrorType.Validation);
    public static readonly Error AlreadyLinked = new("Auth.AlreadyLinked", "This external account is already connected to another user.", ErrorType.Conflict);
    public static readonly Error UserInconsistency = new("Auth.UserInconsistency", "Account inconsistency detected.", ErrorType.Internal);
    public static readonly Error CreateFailed = new("Auth.CreateFailed", "Could not create a new account.", ErrorType.Internal);

    public static readonly Error AccountDeactivated = new("Auth.AccountDeactivated", "Account is deactivated.", ErrorType.Forbidden);

    /// <summary>
    /// Creates an error indicating the account is locked out.
    /// </summary>
    /// <param name="lockoutMinutes">Duration of lockout in minutes.</param>
    public static Error AccountLocked(int lockoutMinutes) =>
        new("Auth.AccountLocked", $"Account is locked. Please try again after {lockoutMinutes} minutes.", ErrorType.Forbidden);
}
