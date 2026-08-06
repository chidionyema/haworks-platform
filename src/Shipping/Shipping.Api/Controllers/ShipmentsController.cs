using System.Text;
using System.Text.Json;
using Haworks.Contracts.Shipping;
using Haworks.Shipping.Api.Domain;
using Haworks.Shipping.Api.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Shipping.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Service")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ShipmentsController(
    ShippingDbContext db,
    IShippingProvider provider,
    IPublishEndpoint publisher,
    ILogger<ShipmentsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions WebhookJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Create shipment and fetch carrier rates.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateShipment([FromBody] CreateShipmentHttpRequest request, CancellationToken ct)
    {
        var validationError = ValidateRequest(request);
        if (validationError != null) return BadRequest(new { error = validationError });

        var result = await provider.CreateShipmentAsync(new CreateShipmentRequest(
            new AddressDto(request.FromStreet, request.FromCity, request.FromState, request.FromZip, request.FromCountry),
            new AddressDto(request.ToStreet, request.ToCity, request.ToState, request.ToZip, request.ToCountry),
            new ParcelDto(request.LengthInches, request.WidthInches, request.HeightInches, request.WeightOz)
        ), ct);

        var shipment = Shipment.Create(request.OrderId, result.ShipmentId);
        shipment.SetAddresses(
            request.FromStreet, request.FromCity, request.FromState, request.FromZip, request.FromCountry,
            request.ToStreet, request.ToCity, request.ToState, request.ToZip, request.ToCountry);

        db.Shipments.Add(shipment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The remote EasyPost shipment (result.ShipmentId) now has no local
            // record. It costs nothing (no label purchased yet) but needs manual
            // reconciliation if it's ever queried against — log it so it isn't
            // silently lost.
            logger.LogError(ex, "Failed to persist Shipment after creating EasyPost shipment {EasyPostShipmentId} for order {OrderId}",
                result.ShipmentId, request.OrderId);
            throw;
        }

        return Ok(new { shipment.Id, shipment.EasyPostShipmentId, result.Rates });
    }

    /// <summary>Buy the selected (or cheapest) rate — generates label.</summary>
    [HttpPost("{id:guid}/buy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BuyLabel(Guid id, [FromBody] BuyLabelRequest request, CancellationToken ct)
    {
        // Phase 1 (atomic): lock the aggregate, reserve it for purchase.
        Shipment shipment;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var locked = await LockShipmentAsync(id, ct);
            if (locked == null) { await tx.RollbackAsync(ct); return NotFound(); }

            if (locked.Status != ShipmentStatus.Created)
            {
                await tx.RollbackAsync(ct);
                return Conflict(new { error = $"Shipment is not in a buyable state ({locked.Status})." });
            }

            locked.BeginPurchase();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            shipment = locked;
        }

        // Phase 2 (I/O): no DB lock held while calling EasyPost.
        BuyLabelResult result;
        try
        {
            result = await provider.BuyLabelAsync(shipment.EasyPostShipmentId, request.RateId, ct);
        }
        catch (RateNotFoundException)
        {
            await RevertReservationAsync(id, ct);
            return BadRequest(new { error = "Selected rate is no longer available." });
        }
        catch
        {
            await RevertReservationAsync(id, ct);
            throw;
        }

        // Phase 3 (atomic): re-lock, finalize.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var locked = await LockShipmentAsync(id, ct);
            if (locked == null) { await tx.RollbackAsync(ct); return NotFound(); }

            var rateCents = (long)Math.Round(result.Amount * 100m, 0, MidpointRounding.AwayFromZero);
            locked.MarkLabelPurchased(
                result.Carrier, result.Service, result.TrackingNumber,
                result.TrackingUrl, result.LabelUrl, rateCents, result.Currency, result.EstimatedDelivery);

            await publisher.Publish(new ShipmentCreatedEvent
            {
                ShipmentId = locked.Id,
                OrderId = locked.OrderId,
                CarrierCode = result.Carrier,
                TrackingNumber = result.TrackingNumber,
                TrackingUrl = result.TrackingUrl,
            }, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            shipment = locked;
        }

        return Ok(new { shipment.TrackingNumber, shipment.TrackingUrl, shipment.LabelUrl, shipment.EstimatedDelivery });
    }

    /// <summary>Get shipment details + tracking.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipment(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        return shipment == null ? NotFound() : Ok(shipment);
    }

    /// <summary>Get shipments for an order.</summary>
    [HttpGet("by-order/{orderId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByOrder(Guid orderId, CancellationToken ct)
    {
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == orderId).OrderByDescending(s => s.CreatedAt).Take(100)
            .ToListAsync(ct);
        return Ok(shipments);
    }

    /// <summary>Refresh tracking details from the carrier on demand.</summary>
    [HttpGet("{id:guid}/tracking/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshTracking(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment == null || string.IsNullOrEmpty(shipment.TrackingNumber)) return NotFound();

        var tracking = await provider.GetTrackingAsync(shipment.TrackingNumber, ct);
        return Ok(tracking);
    }

    /// <summary>EasyPost tracking webhook.</summary>
    [HttpPost("webhooks/easypost")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleWebhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        if (!provider.ValidateWebhookSignature(Encoding.UTF8.GetBytes(rawBody), headers))
        {
            logger.LogWarning("Rejected EasyPost webhook: signature verification failed");
            return Unauthorized();
        }

        var payload = JsonSerializer.Deserialize<EasyPostWebhookPayload>(rawBody, WebhookJsonOptions);
        if (payload == null) return Ok();

        if (!string.Equals(payload.Description, "tracker.updated", StringComparison.Ordinal)
            && !string.Equals(payload.Description, "tracker.created", StringComparison.Ordinal))
            return Ok();

        var trackingCode = payload.Result?.TrackingCode;
        if (string.IsNullOrEmpty(trackingCode)) return Ok();

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.TrackingNumber == trackingCode, ct);
        if (shipment == null) return Ok();

        var newStatus = MapEasyPostStatus(payload.Result?.Status);
        if (newStatus == null)
        {
            logger.LogWarning("Unmapped EasyPost tracker status {Status} for shipment {ShipmentId}", payload.Result?.Status, shipment.Id);
            return Ok();
        }

        if (shipment.Status == newStatus) return Ok();

        shipment.UpdateStatus(newStatus.Value);

        if (newStatus == ShipmentStatus.Delivered)
        {
            await publisher.Publish(new ShipmentDeliveredEvent
            {
                ShipmentId = shipment.Id,
                OrderId = shipment.OrderId,
                DeliveredAt = shipment.DeliveredAt ?? DateTime.UtcNow,
            }, ct);
        }
        else if (newStatus == ShipmentStatus.Exception)
        {
            await publisher.Publish(new ShipmentExceptionEvent
            {
                ShipmentId = shipment.Id,
                OrderId = shipment.OrderId,
                Reason = payload.Result?.StatusDetail ?? "Unknown exception",
            }, ct);
        }

        await db.SaveChangesAsync(ct);

        return Ok();
    }

    private async Task<Shipment?> LockShipmentAsync(Guid id, CancellationToken ct)
    {
        return await db.Shipments
            .FromSqlInterpolated($"SELECT * FROM shipping.\"Shipments\" WHERE \"Id\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
    }

    private async Task RevertReservationAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await LockShipmentAsync(id, ct);
        if (locked == null) { await tx.RollbackAsync(ct); return; }

        locked.RevertPurchase();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string? ValidateRequest(CreateShipmentHttpRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.FromStreet) || r.FromStreet.Length > 200) return "FromStreet is required (max 200 chars).";
        if (string.IsNullOrWhiteSpace(r.FromCity) || r.FromCity.Length > 100) return "FromCity is required (max 100 chars).";
        if (string.IsNullOrWhiteSpace(r.FromState) || r.FromState.Length > 100) return "FromState is required (max 100 chars).";
        if (string.IsNullOrWhiteSpace(r.FromZip) || r.FromZip.Length > 20) return "FromZip is required (max 20 chars).";
        if (string.IsNullOrWhiteSpace(r.FromCountry) || r.FromCountry.Length > 2) return "FromCountry is required (ISO-2).";
        if (string.IsNullOrWhiteSpace(r.ToStreet) || r.ToStreet.Length > 200) return "ToStreet is required (max 200 chars).";
        if (string.IsNullOrWhiteSpace(r.ToCity) || r.ToCity.Length > 100) return "ToCity is required (max 100 chars).";
        if (string.IsNullOrWhiteSpace(r.ToState) || r.ToState.Length > 100) return "ToState is required (max 100 chars).";
        if (string.IsNullOrWhiteSpace(r.ToZip) || r.ToZip.Length > 20) return "ToZip is required (max 20 chars).";
        if (string.IsNullOrWhiteSpace(r.ToCountry) || r.ToCountry.Length > 2) return "ToCountry is required (ISO-2).";
        if (r.LengthInches <= 0) return "LengthInches must be greater than 0.";
        if (r.WidthInches <= 0) return "WidthInches must be greater than 0.";
        if (r.HeightInches <= 0) return "HeightInches must be greater than 0.";
        if (r.WeightOz <= 0) return "WeightOz must be greater than 0.";
        return null;
    }

    private static ShipmentStatus? MapEasyPostStatus(string? status) => status switch
    {
        "in_transit" => ShipmentStatus.InTransit,
        "out_for_delivery" => ShipmentStatus.OutForDelivery,
        "delivered" => ShipmentStatus.Delivered,
        "failure" or "error" => ShipmentStatus.Exception,
        "cancelled" => ShipmentStatus.Cancelled,
        "pre_transit" or "unknown" => null,
        _ => null,
    };
}

public sealed record CreateShipmentHttpRequest(
    [property: System.Text.Json.Serialization.JsonRequired] Guid OrderId,
    string FromStreet, string FromCity, string FromState, string FromZip, string FromCountry,
    string ToStreet, string ToCity, string ToState, string ToZip, string ToCountry,
    [property: System.Text.Json.Serialization.JsonRequired] double LengthInches,
    [property: System.Text.Json.Serialization.JsonRequired] double WidthInches,
    [property: System.Text.Json.Serialization.JsonRequired] double HeightInches,
    [property: System.Text.Json.Serialization.JsonRequired] double WeightOz);

public sealed record BuyLabelRequest(string RateId);

public sealed record EasyPostWebhookPayload(string? Description, EasyPostWebhookResult? Result);
public sealed record EasyPostWebhookResult(string? TrackingCode, string? Status, string? StatusDetail);
