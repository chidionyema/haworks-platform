namespace Haworks.Payments.Infrastructure.Stripe;

internal static class StripeSubscriptionStatusMapper
{
    public static SubscriptionStatus FromStripeStatus(string stripeStatus) => stripeStatus switch
    {
        "active" => SubscriptionStatus.Active,
        "canceled" => SubscriptionStatus.Canceled,
        "unpaid" => SubscriptionStatus.Unpaid,
        "past_due" => SubscriptionStatus.PastDue,
        "trialing" => SubscriptionStatus.Trialing,
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.Expired,
        _ => SubscriptionStatus.Unknown
    };
}
