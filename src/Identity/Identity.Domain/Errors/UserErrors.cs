namespace Haworks.Identity.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class UserErrors
{
    public static readonly Error NotFound = new("Users.NotFound", "User not found.", ErrorType.NotFound);
    public static readonly Error MissingUserId = new("Users.MissingUserId", "User ID is required.", ErrorType.Validation);
    public static readonly Error UpdateFailed = new("Users.UpdateFailed", "Failed to update user profile.", ErrorType.Internal);
    public static readonly Error FetchFailed = new("Users.FetchFailed", "Failed to fetch user profile.", ErrorType.Internal);
}
