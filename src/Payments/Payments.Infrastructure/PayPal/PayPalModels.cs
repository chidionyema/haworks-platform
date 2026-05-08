using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haworks.Payments.Infrastructure.PayPal;

// ============================================================================
// PayPal Order API Models
// ============================================================================

internal sealed class PayPalOrderRequest
{
    public string Intent { get; set; } = "CAPTURE";
    public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = new();
    public PayPalApplicationContext? ApplicationContext { get; set; }
}

internal sealed class PayPalPurchaseUnit
{
    public string? ReferenceId { get; set; }
    public PayPalAmount? Amount { get; set; }
    public string? Description { get; set; }
    public string? CustomId { get; set; }
    public PayPalPayments? Payments { get; set; }
}

internal sealed class PayPalAmount
{
    public string CurrencyCode { get; set; } = "USD";
    public string Value { get; set; } = "0.00";
}

internal sealed class PayPalApplicationContext
{
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
    public string? BrandName { get; set; }
    public string? UserAction { get; set; }
}

internal sealed class PayPalOrder
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
    public PayPalPayer? Payer { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

internal sealed class PayPalPayer
{
    public string? PayerId { get; set; }
    public string? EmailAddress { get; set; }
}

internal sealed class PayPalLink
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

internal sealed class PayPalPayments
{
    public List<PayPalCapture>? Captures { get; set; }
}

internal sealed class PayPalCapture
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalAmount? Amount { get; set; }
}

// ============================================================================
// PayPal Subscription API Models
// ============================================================================

internal sealed class PayPalSubscriptionRequest
{
    public string? PlanId { get; set; }
    public PayPalApplicationContext? ApplicationContext { get; set; }
    public PayPalSubscriber? Subscriber { get; set; }
}

internal sealed class PayPalSubscriber
{
    public string? EmailAddress { get; set; }
}

internal sealed class PayPalSubscription
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? PlanId { get; set; }
    public List<PayPalLink>? Links { get; set; }
}

// ============================================================================
// PayPal Refund API Models
// ============================================================================

internal sealed class PayPalRefundRequest
{
    public PayPalRefundAmount? Amount { get; set; }
    public string? NoteToPayer { get; set; }
}

internal sealed class PayPalRefundAmount
{
    public string CurrencyCode { get; set; } = "USD";
    public string Value { get; set; } = "0.00";
}

internal sealed class PayPalRefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public PayPalRefundAmount? Amount { get; set; }
    public PayPalStatusDetails? StatusDetails { get; set; }
}

internal sealed class PayPalStatusDetails
{
    public string? Reason { get; set; }
}

// ============================================================================
// PayPal Error Models
// ============================================================================

internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Field { get; set; }
    public string? Description { get; set; }
}

// ============================================================================
// PayPal Webhook Models
// ============================================================================

internal sealed class PayPalVerifySignatureRequest
{
    public string? WebhookId { get; set; }
    public string? TransmissionId { get; set; }
    public string? TransmissionTime { get; set; }
    public string? TransmissionSig { get; set; }
    public string? CertUrl { get; set; }
    public string? AuthAlgo { get; set; }
    public JsonDocument? WebhookEvent { get; set; }
}

internal sealed class PayPalVerifySignatureResponse
{
    public string? VerificationStatus { get; set; }
}

internal sealed class PayPalSignatureHeaders
{
    public string TransmissionId { get; set; } = string.Empty;
    public string TransmissionTime { get; set; } = string.Empty;
    public string TransmissionSig { get; set; } = string.Empty;
    public string CertUrl { get; set; } = string.Empty;
    public string AuthAlgo { get; set; } = string.Empty;
}

internal sealed class PayPalWebhookEvent
{
    public string? Id { get; set; }
    public string? EventType { get; set; }
    public string? CreateTime { get; set; }
    public string? ResourceType { get; set; }
    public JsonElement? Resource { get; set; }
    public string? Summary { get; set; }
}

// ============================================================================
// PayPal OAuth Models
// ============================================================================

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;
}

// ============================================================================
// PayPal Subscription Management Models
// ============================================================================

internal sealed class PayPalCancelSubscriptionRequest
{
    public string? Reason { get; set; }
}

internal sealed class PayPalSubscriptionResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public string? PlanId { get; set; }
    public PayPalBillingInfo? BillingInfo { get; set; }
    public PayPalSubscriber? Subscriber { get; set; }
}

internal sealed class PayPalBillingInfo
{
    public string? NextBillingTime { get; set; }
    public string? LastPaymentTime { get; set; }
    public PayPalLastPayment? LastPayment { get; set; }
}

internal sealed class PayPalLastPayment
{
    public PayPalAmountResponse? Amount { get; set; }
    public string? Time { get; set; }
}

internal sealed class PayPalAmountResponse
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal static class PayPalJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
