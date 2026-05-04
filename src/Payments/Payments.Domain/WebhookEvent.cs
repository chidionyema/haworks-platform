using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Payments.Domain;

public enum HandlerType
{
    Payment,
    Subscription,
    Unknown
}

public class WebhookEvent : AuditableEntity
{
    protected WebhookEvent() : base() { }

    private WebhookEvent(
        PaymentProvider provider,
        string providerEventId,
        string eventType,
        string eventJson,
        HandlerType handlerType) : base()
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        EventJson = eventJson;
        HandlerType = handlerType;
        IsProcessed = false;
    }

    public PaymentProvider Provider { get; private set; } = PaymentProvider.Stripe;
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string EventJson { get; private set; } = string.Empty;
    public DateTime? ProcessedAt { get; private set; }
    public bool IsProcessed { get; private set; }
    public HandlerType HandlerType { get; private set; } = HandlerType.Unknown;
    public string Error { get; private set; } = string.Empty;

    public static WebhookEvent Create(
        PaymentProvider provider,
        string providerEventId,
        string eventType,
        string eventJson,
        HandlerType handlerType = HandlerType.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new WebhookEvent(provider, providerEventId, eventType, eventJson, handlerType);
    }

    public void MarkProcessed()
    {
        IsProcessed = true;
        ProcessedAt = DateTime.UtcNow;
        Error = string.Empty;
    }

    public void MarkFailed(string error)
    {
        IsProcessed = false;
        ProcessedAt = DateTime.UtcNow;
        Error = error;
    }
}
