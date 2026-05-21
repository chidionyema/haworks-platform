using MassTransit;

namespace Haworks.Privacy.Application.Requests.Sagas;

public class PrivacyRequestState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public int Version { get; set; }

    public Guid UserId { get; set; }
    public string RequestType { get; set; } = null!;

    // Tracking completion per service
    public bool IdentityCompleted { get; set; }
    public bool OrdersCompleted { get; set; }
    public bool PaymentsCompleted { get; set; }

    // H9: per-service completion timestamps (GDPR audit trail)
    public DateTime? IdentityCompletedAt { get; set; }
    public DateTime? OrdersCompletedAt { get; set; }
    public DateTime? PaymentsCompletedAt { get; set; }

    // C2: comma-separated list of services that reported failure (e.g. "Orders,Payments")
    public string? FailedServices { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // PR-02: timeout schedule token
    public Guid? ErasureTimeoutTokenId { get; set; }

    // Helper: set of failed service names
    public IReadOnlySet<string> FailedServicesSet =>
        string.IsNullOrEmpty(FailedServices)
            ? (IReadOnlySet<string>)new HashSet<string>()
            : new HashSet<string>(FailedServices.Split(',', StringSplitOptions.RemoveEmptyEntries));
}
