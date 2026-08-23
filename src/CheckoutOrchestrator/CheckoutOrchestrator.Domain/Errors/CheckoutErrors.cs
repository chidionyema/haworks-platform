namespace Haworks.CheckoutOrchestrator.Domain.Errors;
using Haworks.BuildingBlocks.Common;

public static class CheckoutErrors
{
    public static readonly Error SessionNotFound = new("Checkout.SessionNotFound", "Checkout session not found.", ErrorType.NotFound);
    public static readonly Error FetchFailed = new("Checkout.FetchFailed", "Failed to fetch checkout session.", ErrorType.Internal);
    public static readonly Error CreationFailed = new("Checkout.CreationFailed", "Failed to create checkout session.", ErrorType.Internal);
}
