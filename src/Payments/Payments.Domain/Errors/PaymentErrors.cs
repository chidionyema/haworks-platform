namespace Haworks.Payments.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class PaymentErrors
{
    public static readonly Error DuplicateOrder = new("Payment.Duplicate", "Duplicate order detected");
    public static readonly Error InsufficientStock = new("Payment.InsufficientStock", "Insufficient stock for one or more products");
    public static readonly Error SessionNotFound = new("Payment.SessionNotFound", "Payment session not found");
    public static readonly Error ProductNotFound = new("Payment.ProductNotFound", "Product not found");
    public static readonly Error InvalidSession = new("Payment.InvalidSession", "Invalid or expired payment session");
    public static readonly Error PaymentFailed = new("Payment.Failed", "Payment processing failed");
    public static readonly Error OrderNotFound = new("Payment.OrderNotFound", "Order not found");
    public static readonly Error ProcessingError = new("Payment.ProcessingError", "An error occurred while processing the payment");
    public static readonly Error SessionCreationFailed = new("Payment.SessionCreationFailed", "Order created but payment session failed to initialize");
}

public static class SubscriptionErrors
{
    public static readonly Error InvalidUser = new("Subscription.InvalidUser", "User ID is required.", ErrorType.Validation);
    public static readonly Error InvalidPriceId = new("Subscription.InvalidPriceId", "Price ID is required.", ErrorType.Validation);
    public static readonly Error SessionCreationFailed = new("Subscription.SessionCreationFailed", "Failed to create subscription session.", ErrorType.Internal);
    public static readonly Error InvalidRequest = new("Subscription.InvalidRequest", "Invalid subscription request.", ErrorType.Validation);
    public static readonly Error StripeError = new("Subscription.StripeError", "Stripe API error.", ErrorType.Internal);
    public static readonly Error FetchFailed = new("Subscription.FetchFailed", "Failed to fetch subscription status.", ErrorType.Internal);
}
