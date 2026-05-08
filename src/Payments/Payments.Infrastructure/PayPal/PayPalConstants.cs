namespace Haworks.Payments.Infrastructure.PayPal;

internal static class PayPalEndpoints
{
    public const string OAuthToken = "/v1/oauth2/token";
    public const string CheckoutOrders = "/v2/checkout/orders";
    public const string Subscriptions = "/v1/billing/subscriptions";
    public const string VerifyWebhookSignature = "/v1/notifications/verify-webhook-signature";

    public static string GetOrder(string orderId) => $"/v2/checkout/orders/{orderId}";
    public static string CaptureOrder(string orderId) => $"/v2/checkout/orders/{orderId}/capture";
    public static string GetRefund(string refundId) => $"/v2/payments/refunds/{refundId}";
    public static string RefundCapture(string captureId) => $"/v2/payments/captures/{captureId}/refund";
    public static string GetSubscription(string subscriptionId) => $"/v1/billing/subscriptions/{subscriptionId}";
    public static string CancelSubscription(string subscriptionId) => $"/v1/billing/subscriptions/{subscriptionId}/cancel";
    public static string ActivateSubscription(string subscriptionId) => $"/v1/billing/subscriptions/{subscriptionId}/activate";
}

internal static class PayPalOrderStatuses
{
    public const string Created = "CREATED";
    public const string Approved = "APPROVED";
    public const string Completed = "COMPLETED";
    public const string Voided = "VOIDED";
}

internal static class PayPalSubscriptionStatuses
{
    public const string Active = "ACTIVE";
    public const string Cancelled = "CANCELLED";
    public const string Suspended = "SUSPENDED";
    public const string Expired = "EXPIRED";
    public const string ApprovalPending = "APPROVAL_PENDING";
}

internal static class PayPalEventTypes
{
    public const string CheckoutOrderApproved = "CHECKOUT.ORDER.APPROVED";
    public const string PaymentCaptureCompleted = "PAYMENT.CAPTURE.COMPLETED";
    public const string PaymentCaptureDenied = "PAYMENT.CAPTURE.DENIED";
    public const string PaymentCaptureRefunded = "PAYMENT.CAPTURE.REFUNDED";
    public const string BillingSubscriptionCreated = "BILLING.SUBSCRIPTION.CREATED";
    public const string BillingSubscriptionActivated = "BILLING.SUBSCRIPTION.ACTIVATED";
    public const string BillingSubscriptionUpdated = "BILLING.SUBSCRIPTION.UPDATED";
    public const string BillingSubscriptionCancelled = "BILLING.SUBSCRIPTION.CANCELLED";
    public const string BillingSubscriptionExpired = "BILLING.SUBSCRIPTION.EXPIRED";
    public const string BillingSubscriptionSuspended = "BILLING.SUBSCRIPTION.SUSPENDED";
    public const string BillingSubscriptionReactivated = "BILLING.SUBSCRIPTION.RE-ACTIVATED";
    public const string BillingSubscriptionPaymentFailed = "BILLING.SUBSCRIPTION.PAYMENT.FAILED";
}

internal static class PayPalVerificationStatuses
{
    public const string Success = "SUCCESS";
    public const string Failure = "FAILURE";
}

internal static class CheckoutConstants
{
    public const decimal CentMultiplier = 100m;
}

internal static class ProviderNames
{
    public const string PayPal = "PayPal";
}
