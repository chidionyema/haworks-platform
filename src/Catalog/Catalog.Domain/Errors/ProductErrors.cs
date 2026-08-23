namespace Haworks.Catalog.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class ProductErrors
{
    public static readonly Error NotFound = new("Products.NotFound", "Product not found.", ErrorType.NotFound);
    public static readonly Error InvalidCategory = new("Products.InvalidCategory", "Invalid category ID.", ErrorType.Validation);
    public static readonly Error CreateFailed = new("Products.CreateFailed", "Failed to create product.", ErrorType.Internal);
    public static readonly Error UpdateFailed = new("Products.UpdateFailed", "Failed to update product.", ErrorType.Internal);
    public static readonly Error DeleteFailed = new("Products.DeleteFailed", "Failed to delete product.", ErrorType.Internal);

    // Factory methods for dynamic messages
    public static Error NotFoundWithId(Guid productId) =>
        new("Products.NotFound", $"Product with ID {productId} not found.", ErrorType.NotFound);
}
