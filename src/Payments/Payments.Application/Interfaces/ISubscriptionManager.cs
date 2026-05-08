using Haworks.Contracts.Payments;

namespace Haworks.Payments.Application.Interfaces;

public interface ISubscriptionManager
{
    Task<SubscriptionStatusResult> GetStatusAsync(string userId, CancellationToken ct = default);
    Task<bool> CancelAsync(string subscriptionId, bool immediate = false, CancellationToken ct = default);
    Task<bool> ResumeAsync(string subscriptionId, CancellationToken ct = default);
    Task HandleSubscriptionEventAsync(SubscriptionEvent subscriptionEvent, CancellationToken ct = default);
}

public record SubscriptionStatusResult
{
    public bool IsActive { get; init; }
    public string? SubscriptionId { get; init; }
    public string? PlanId { get; init; }
    public SubscriptionStatus Status { get; init; }
    public DateTime? CurrentPeriodEnd { get; init; }
    public DateTime? CanceledAt { get; init; }
    public PaymentProvider Provider { get; init; }
}

public record SubscriptionEvent
{
    public required string SubscriptionId { get; init; }
    public required SubscriptionEventType EventType { get; init; }
    public required SubscriptionStatus NewStatus { get; init; }
    public string? UserId { get; init; }
    public string? PlanId { get; init; }
    public DateTime? CurrentPeriodEnd { get; init; }
    public PaymentProvider Provider { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public enum SubscriptionEventType { Created, Updated, Canceled, Renewed, PaymentFailed, Paused, Resumed, Expired }
