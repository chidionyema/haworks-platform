using System.ComponentModel.DataAnnotations;

namespace Haworks.Shipping.Api.Domain;

public enum ShipmentStatus
{
    Created,
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
    [MaxLength(50)]
    public string EasyPostShipmentId { get; private set; } = string.Empty;
    public ShipmentStatus Status { get; private set; }
    [MaxLength(20)]
    public string CarrierCode { get; private set; } = string.Empty;
    [MaxLength(50)]
    public string ServiceLevel { get; private set; } = string.Empty;
    [MaxLength(100)]
    public string TrackingNumber { get; private set; } = string.Empty;
    [MaxLength(500)]
    public string TrackingUrl { get; private set; } = string.Empty;
    [MaxLength(500)]
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

    public void MarkLabelPurchased(string carrier, string service, string trackingNumber, string trackingUrl, string labelUrl, long rateCents, string currency, DateTime? estimatedDelivery)
    {
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

    public void UpdateStatus(ShipmentStatus newStatus)
    {
        if (!IsValidTransition(Status, newStatus))
            throw new InvalidOperationException($"Invalid status transition from {Status} to {newStatus}");

        Status = newStatus;
        if (newStatus == ShipmentStatus.Delivered)
            DeliveredAt = DateTime.UtcNow;
    }

    private static bool IsValidTransition(ShipmentStatus current, ShipmentStatus target)
    {
        return current switch
        {
            ShipmentStatus.Created => target is ShipmentStatus.LabelPurchased or ShipmentStatus.Cancelled,
            ShipmentStatus.LabelPurchased => target is ShipmentStatus.InTransit or ShipmentStatus.Exception or ShipmentStatus.Cancelled,
            ShipmentStatus.InTransit => target is ShipmentStatus.OutForDelivery or ShipmentStatus.Delivered or ShipmentStatus.Exception,
            ShipmentStatus.OutForDelivery => target is ShipmentStatus.Delivered or ShipmentStatus.Exception,
            ShipmentStatus.Delivered => false, // Terminal state
            ShipmentStatus.Exception => target is ShipmentStatus.InTransit or ShipmentStatus.Cancelled,
            ShipmentStatus.Cancelled => false, // Terminal state
            _ => false
        };
    }

    public void SetAddresses(string fromStreet, string fromCity, string fromState, string fromZip, string fromCountry, string toStreet, string toCity, string toState, string toZip, string toCountry)
    {
        FromStreet = fromStreet; FromCity = fromCity; FromState = fromState; FromZip = fromZip; FromCountry = fromCountry;
        ToStreet = toStreet; ToCity = toCity; ToState = toState; ToZip = toZip; ToCountry = toCountry;
    }
}
