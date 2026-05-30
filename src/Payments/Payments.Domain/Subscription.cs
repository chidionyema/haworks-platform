using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Payments.Domain;

public class Subscription : AuditableEntity
{
    protected Subscription() : base() { }

    private Subscription(
        string userId,
        PaymentProvider provider,
        string providerSubscriptionId,
        string planId,
        DateTime startsAt,
        DateTime expiresAt) : base()
    {
        UserId = userId;
        Provider = provider;
        ProviderSubscriptionId = providerSubscriptionId;
        PlanId = planId;
        Status = SubscriptionStatus.Incomplete;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
    }

    public string UserId { get; private set; } = string.Empty;
    public PaymentProvider Provider { get; private set; } = PaymentProvider.Stripe;
    public string ProviderSubscriptionId { get; private set; } = string.Empty;
    public string PlanId { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public DateTime StartsAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? CanceledAt { get; private set; }

    public static Subscription Create(
        string userId,
        PaymentProvider provider,
        string providerSubscriptionId,
        string planId,
        DateTime startsAt,
        DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);

        return new Subscription(userId, provider, providerSubscriptionId, planId, startsAt, expiresAt);
    }

    public void Activate() => Status = SubscriptionStatus.Active;
    public void UpdateStatus(SubscriptionStatus status) => Status = status;
    public void Cancel() { Status = SubscriptionStatus.Canceled; CanceledAt = DateTime.UtcNow; }
    public void ClearCancellation() => CanceledAt = null;
    public void SetExpiresAt(DateTime expiresAt) => ExpiresAt = expiresAt;
    public bool IsActive => Status == SubscriptionStatus.Active && DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// Anonymises PII fields for GDPR erasure.
    /// </summary>
    public void AnonymiseForPrivacy()
    {
        UserId = "ERASED";
    }
}

public class SubscriptionPlan : AuditableEntity
{
    protected SubscriptionPlan() : base() { }

    private SubscriptionPlan(string name, string internalPlanId, long priceCents, string currency, string? description) : base()
    {
        Name = name;
        InternalPlanId = internalPlanId;
        PriceCents = priceCents;
        Currency = currency;
        Description = description;
        ProviderPriceIds = "{}";
    }

    public string Name { get; private set; } = string.Empty;
    public string InternalPlanId { get; private set; } = string.Empty;
    public string ProviderPriceIds { get; private set; } = "{}";
    public long PriceCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public static SubscriptionPlan Create(string name, string internalPlanId, long priceCents, string currency, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(internalPlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (priceCents < 0) throw new ArgumentException("Price cannot be negative", nameof(priceCents));
        return new SubscriptionPlan(name, internalPlanId, priceCents, currency, description);
    }

    public void SetProviderPriceIds(string providerPriceIdsJson) => ProviderPriceIds = providerPriceIdsJson ?? "{}";
}

public enum SubscriptionStatus
{
    Active,
    Canceled,
    Incomplete,
    Unknown,
    PastDue,
    Trialing,
    Expired,
    Unpaid
}
