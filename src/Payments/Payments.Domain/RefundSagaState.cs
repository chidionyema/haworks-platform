using MassTransit;

namespace Haworks.Payments.Domain;

public class RefundSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }   // SagaId = RefundId
    public string CurrentState { get; set; } = "";
    public int Version { get; set; }

    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid RefundId { get; set; }        // mirrored from CorrelationId for clarity
    // TRACKED: Amount is decimal here; tracked for migration to long (AmountCents) in the
    // platform-wide AmountCents migration (see MEMORY.md — AmountCents Migration section).
    // Do not change the type until that migration branch lands.
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Reason { get; set; } = "";  // customer-cited reason, free-form
    public string Provider { get; set; } = ""; // "Stripe" | "PayPal"
    public string? ProviderRefundId { get; set; }  // populated post-ProviderRefundInitiated
    public string? FailureDetail { get; set; }
    public RefundFailureCategory FailureCategory { get; set; }
    public string? RequestedBy { get; set; }  // UserId or service identity that initiated the refund
    public Guid? RefundTimeoutTokenId { get; set; }
    public Guid? ReviewEscalationTokenId { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
