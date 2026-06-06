using EasyPost;
using EasyPost.Models.API;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Haworks.Shipping.Api.Infrastructure;

public sealed class EasyPostOptions
{
    public const string SectionName = "EasyPost";
    public string ApiKey { get; set; } = string.Empty;
    public bool TestMode { get; set; } = true;
}

public sealed class EasyPostShippingProvider : IShippingProvider
{
    private readonly Client _client;
    private readonly ILogger<EasyPostShippingProvider> _logger;

    public EasyPostShippingProvider(IOptions<EasyPostOptions> opts, ILogger<EasyPostShippingProvider> logger)
    {
        _client = new Client(new ClientConfiguration(opts.Value.ApiKey));
        _logger = logger;
    }

    public async Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct)
    {
        try
        {
            var shipment = await _client.Shipment.Create(new EasyPost.Parameters.Shipment.Create
            {
                FromAddress = new EasyPost.Parameters.Address.Create
                {
                    Street1 = request.From.Street,
                    City = request.From.City,
                    State = request.From.State,
                    Zip = request.From.Zip,
                    Country = request.From.Country,
                },
                ToAddress = new EasyPost.Parameters.Address.Create
                {
                    Street1 = request.To.Street,
                    City = request.To.City,
                    State = request.To.State,
                    Zip = request.To.Zip,
                    Country = request.To.Country,
                },
                Parcel = new EasyPost.Parameters.Parcel.Create
                {
                    Length = request.Parcel.LengthInches,
                    Width = request.Parcel.WidthInches,
                    Height = request.Parcel.HeightInches,
                    Weight = request.Parcel.WeightOz,
                },
            }, ct);

        var rates = shipment.Rates?.Select(r =>
        {
            if (!decimal.TryParse(r.Price, out var amt))
            {
                _logger.LogWarning("Failed to parse rate price '{Price}' for carrier {Carrier}, rate ID {RateId}",
                    r.Price, r.Carrier, r.Id);
                throw new InvalidOperationException($"Invalid rate price format: {r.Price}");
            }
            return new RateDto(
                r.Id ?? "unknown",
                r.Carrier ?? "unknown",
                r.Service ?? "standard",
                amt,
                r.Currency ?? string.Empty,
                r.DeliveryDays);
        }).ToList() ?? [];

            _logger.LogInformation("Created EasyPost shipment {ShipmentId} with {RateCount} rates",
                shipment.Id, rates.Count);

            return new CreateShipmentResult(shipment.Id ?? "", rates);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error creating EasyPost shipment for addresses {FromCity}-{ToCity}",
                request.From.City, request.To.City);
            throw new InvalidOperationException("Failed to create shipment due to network error", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout creating EasyPost shipment for addresses {FromCity}-{ToCity}",
                request.From.City, request.To.City);
            throw new InvalidOperationException("Shipment creation timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating EasyPost shipment for addresses {FromCity}-{ToCity}",
                request.From.City, request.To.City);
            throw new InvalidOperationException("Failed to create shipment", ex);
        }
    }

    public async Task<BuyLabelResult> BuyLabelAsync(string shipmentId, string rateId, CancellationToken ct)
    {
        try
        {
            var shipment = await _client.Shipment.Retrieve(shipmentId, ct);
            var rate = shipment.Rates?.FirstOrDefault(r => string.Equals(r.Id, rateId, StringComparison.Ordinal));
            if (rate == null)
            {
                _logger.LogWarning("Rate {RateId} not found for shipment {ShipmentId}. Available rates: {AvailableRates}",
                    rateId, shipmentId, string.Join(", ", shipment.Rates?.Select(r => r.Id) ?? []));
                throw new ArgumentException($"Rate {rateId} not found for shipment {shipmentId}");
            }

#pragma warning disable CA2016, HWK050 // EasyPost SDK Buy() does not accept CancellationToken
            var bought = await _client.Shipment.Buy(shipmentId, rate.Id);
#pragma warning restore CA2016, HWK050

        if (!decimal.TryParse(rate.Price, out var amt))
        {
            _logger.LogWarning("Failed to parse rate price '{Price}' for purchased label, carrier {Carrier}, rate ID {RateId}",
                rate.Price, rate.Carrier, rate.Id);
            throw new InvalidOperationException($"Invalid rate price format during label purchase: {rate.Price}");
        }

            return new BuyLabelResult(
                bought.TrackingCode ?? "",
                bought.Tracker?.PublicUrl ?? "",
                bought.PostageLabel?.LabelUrl ?? "",
                rate.Carrier ?? "unknown",
                rate.Service ?? "standard",
                amt,
                rate.Currency ?? string.Empty,
                bought.Tracker?.EstDeliveryDate);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error buying label for shipment {ShipmentId}, rate {RateId}",
                shipmentId, rateId);
            throw new InvalidOperationException("Failed to buy shipping label due to network error", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout buying label for shipment {ShipmentId}, rate {RateId}",
                shipmentId, rateId);
            throw new InvalidOperationException("Shipping label purchase timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error buying label for shipment {ShipmentId}, rate {RateId}",
                shipmentId, rateId);
            throw new InvalidOperationException("Failed to buy shipping label", ex);
        }
    }

    public async Task<TrackingInfo> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        try
        {
            var tracker = await _client.Tracker.Create(new EasyPost.Parameters.Tracker.Create
            {
                TrackingCode = trackingNumber,
            }, ct);

            var events = tracker.TrackingDetails?.Select(d => new TrackingEvent
            {
                Timestamp = d.Datetime ?? DateTime.UtcNow,
                Status = d.Status ?? "unknown",
                Location = d.TrackingLocation?.City,
                Description = d.Message,
            }).ToList() ?? [];

            return new TrackingInfo(
                tracker.Status ?? "unknown",
                tracker.StatusDetail,
                tracker.EstDeliveryDate,
                events);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error getting tracking info for {TrackingNumber}",
                trackingNumber);
            throw new InvalidOperationException("Failed to get tracking information due to network error", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout getting tracking info for {TrackingNumber}",
                trackingNumber);
            throw new InvalidOperationException("Tracking information request timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting tracking info for {TrackingNumber}",
                trackingNumber);
            throw new InvalidOperationException("Failed to get tracking information", ex);
        }
    }
}
