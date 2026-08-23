namespace Haworks.Orders.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class OrderErrors
{
    public static readonly Error NotFound = new("Orders.NotFound", "Order not found.", ErrorType.NotFound);
    public static readonly Error Forbidden = new("Orders.Forbidden", "You are not authorized to view this order.", ErrorType.Forbidden);
    public static readonly Error NoItems = new("Orders.NoItems", "No items provided for checkout.", ErrorType.Validation);
    public static readonly Error MissingGuestInfo = new("Orders.MissingGuestInfo", "Guest checkout requires shipping information.", ErrorType.Validation);
    public static readonly Error IncompleteGuestInfo = new("Orders.IncompleteGuestInfo", "Incomplete guest information provided.", ErrorType.Validation);
    public static readonly Error InvalidEmail = new("Orders.InvalidEmail", "Invalid email format.", ErrorType.Validation);
    public static readonly Error InvalidUser = new("Orders.InvalidUser", "User ID is required.", ErrorType.Validation);
    public static readonly Error InvalidRequest = new("Orders.InvalidRequest", "Invalid request parameters.", ErrorType.Validation);
    public static readonly Error InvalidSession = new("Orders.InvalidSession", "Invalid session ID.", ErrorType.Validation);
    public static readonly Error PaymentNotFound = new("Orders.PaymentNotFound", "Payment record not found.", ErrorType.NotFound);
    public static readonly Error OrderNotFound = new("Orders.OrderNotFound", "Order not found for the provided session.", ErrorType.NotFound);
    public static readonly Error RetrievalFailed = new("Orders.RetrievalFailed", "An error occurred while retrieving the order.", ErrorType.Internal);
    public static readonly Error VerificationFailed = new("Orders.VerificationFailed", "An error occurred while verifying the order.", ErrorType.Internal);
    public static readonly Error FetchFailed = new("Orders.FetchFailed", "Failed to fetch order.", ErrorType.Internal);

    // Factory methods for dynamic messages
    public static Error NotFoundWithId(Guid orderId) =>
        new("Orders.NotFound", $"Order with ID {orderId} not found.", ErrorType.NotFound);
}
