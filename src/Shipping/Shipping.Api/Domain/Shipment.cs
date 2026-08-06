namespace Haworks.Shipping.Api.Domain;

public enum ShipmentStatus
{
    Created,
    Purchasing,
    LabelPurchased,
    InTransit,
    OutForDelivery,
    Delivered,
    Exception,
    Cancelled
}

public sealed class Shipment
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string EasyPostShipmentId { get; private set; } = string.Empty;
    public ShipmentStatus Status { get; private set; }
    public string CarrierCode { get; private set; } = string.Empty;
    public string ServiceLevel { get; private set; } = string.Empty;
    public string TrackingNumber { get; private set; } = string.Empty;
    public string TrackingUrl { get; private set; } = string.Empty;
    public string LabelUrl { get; private set; } = string.Empty;
    public long RateAmountCents { get; private set; }
    public string RateCurrency { get; private set; } = "USD";
    public string FromStreet { get; private set; } = string.Empty;
    public string FromCity { get; private set; } = string.Empty;
    public string FromState { get; private set; } = string.Empty;
    public string FromZip { get; private set; } = string.Empty;
    public string FromCountry { get; private set; } = "US";
    public string ToStreet { get; private set; } = string.Empty;
    public string ToCity { get; private set; } = string.Empty;
    public string ToState { get; private set; } = string.Empty;
    public string ToZip { get; private set; } = string.Empty;
    public string ToCountry { get; private set; } = "US";
    public DateTime CreatedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? EstimatedDelivery { get; private set; }

    private Shipment() { }

    public static Shipment Create(Guid orderId, string easyPostId)
    {
        return new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            EasyPostShipmentId = easyPostId,
            Status = ShipmentStatus.Created,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static readonly ShipmentStatus[] TerminalStatuses =
        [ShipmentStatus.Delivered, ShipmentStatus.Cancelled];

    /// <summary>Phase 1 of the buy-label gateway: reserve the shipment for purchase.
    /// Throws if a purchase is already in flight or already completed, so a
    /// concurrent/retried request cannot pass the guard twice.</summary>
    public void BeginPurchase()
    {
        if (Status != ShipmentStatus.Created)
            throw new InvalidOperationException($"Cannot begin purchase from status {Status}.");
        Status = ShipmentStatus.Purchasing;
    }

    /// <summary>Phase 2 failed (EasyPost call errored) — release the reservation
    /// so the buy can be retried.</summary>
    public void RevertPurchase()
    {
        if (Status == ShipmentStatus.Purchasing)
            Status = ShipmentStatus.Created;
    }

    public void MarkLabelPurchased(string carrier, string service, string trackingNumber, string trackingUrl, string labelUrl, long rateCents, string currency, DateTime? estimatedDelivery)
    {
        if (Status != ShipmentStatus.Purchasing)
            throw new InvalidOperationException($"Cannot mark label purchased from status {Status}.");

        CarrierCode = carrier;
        ServiceLevel = service;
        TrackingNumber = trackingNumber;
        TrackingUrl = trackingUrl;
        LabelUrl = labelUrl;
        RateAmountCents = rateCents;
        RateCurrency = currency;
        EstimatedDelivery = estimatedDelivery;
        ShippedAt = DateTime.UtcNow;
        Status = ShipmentStatus.LabelPurchased;
    }

    /// <summary>Rejects regressions out of terminal states (Delivered/Cancelled)
    /// so a late or duplicate webhook can't resurrect a finished shipment.</summary>
    public void UpdateStatus(ShipmentStatus newStatus)
    {
        if (TerminalStatuses.Contains(Status))
            return;

        Status = newStatus;
        if (newStatus == ShipmentStatus.Delivered)
            DeliveredAt = DateTime.UtcNow;
    }

    public void SetAddresses(string fromStreet, string fromCity, string fromState, string fromZip, string fromCountry, string toStreet, string toCity, string toState, string toZip, string toCountry)
    {
        FromStreet = fromStreet; FromCity = fromCity; FromState = fromState; FromZip = fromZip; FromCountry = fromCountry;
        ToStreet = toStreet; ToCity = toCity; ToState = toState; ToZip = toZip; ToCountry = toCountry;
    }
}
