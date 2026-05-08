namespace Haworks.Payments.Application.Interfaces;

public interface IRefundService
{
    Task<RefundResult> CreateRefundAsync(RefundRequest request, CancellationToken ct = default);
    Task<RefundResult> GetRefundStatusAsync(string refundId, CancellationToken ct = default);
}

public record RefundRequest
{
    public required string TransactionId { get; init; }
    public long? AmountCents { get; init; }
    public string? Currency { get; init; }
    public string? Reason { get; init; }
    public string? IdempotencyKey { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public record RefundResult
{
    public required string RefundId { get; init; }
    public required RefundStatus Status { get; init; }
    public long AmountCents { get; init; }
    public string? FailureReason { get; init; }
    public PaymentProvider Provider { get; init; }
}

public enum RefundStatus { Pending, Succeeded, Failed, Canceled, RequiresAction }
