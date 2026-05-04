namespace Haworks.Payments.Infrastructure.Gateways.Stripe;

internal static class StripeConstants
{
    public static class SessionModes
    {
        public const string Payment = "payment";
        public const string Subscription = "subscription";
    }

    public static class SessionStatuses
    {
        public const string Open = "open";
        public const string Complete = "complete";
        public const string Expired = "expired";
    }

    public static class PaymentStatuses
    {
        public const string Paid = "paid";
    }
}
