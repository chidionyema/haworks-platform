namespace Haworks.Catalog.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class CategoryErrors
{
    public static readonly Error NotFound = new("Categories.NotFound", "Category not found.", ErrorType.NotFound);
    public static readonly Error InvalidName = new("Categories.InvalidName", "Category name is required.", ErrorType.Validation);

    // Factory methods for dynamic messages
    public static Error NotFoundWithId(Guid categoryId) =>
        new("Categories.NotFound", $"Category with ID {categoryId} not found.", ErrorType.NotFound);
}
