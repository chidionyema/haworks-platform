namespace Haworks.Catalog.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class ReviewErrors
{
    public static readonly Error NotFound = new("Reviews.NotFound", "Review not found.", ErrorType.NotFound);
    public static readonly Error ProductNotFound = new("Reviews.ProductNotFound", "Product not found.", ErrorType.NotFound);
    public static readonly Error InvalidProduct = new("Reviews.InvalidProduct", "Review does not belong to the specified product.", ErrorType.Validation);
    public static readonly Error Forbidden = new("Reviews.Forbidden", "You are not authorized to modify this review.", ErrorType.Forbidden);
    public static readonly Error CreateFailed = new("Reviews.CreateFailed", "Failed to create review.", ErrorType.Internal);
    public static readonly Error UpdateFailed = new("Reviews.UpdateFailed", "Failed to update review.", ErrorType.Internal);
    public static readonly Error DeleteFailed = new("Reviews.DeleteFailed", "Failed to delete review.", ErrorType.Internal);
    public static readonly Error ApproveFailed = new("Reviews.ApproveFailed", "Failed to approve review.", ErrorType.Internal);
    public static readonly Error FetchFailed = new("Reviews.FetchFailed", "Failed to fetch reviews.", ErrorType.Internal);

    // Factory methods for dynamic messages
    public static Error ProductNotFoundWithId(Guid productId) =>
        new("Reviews.ProductNotFound", $"Product with ID {productId} not found.", ErrorType.NotFound);
}
