using EasyPost;
using EasyPost.Exceptions.General;
using EasyPost.Models.API;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Haworks.Shipping.Api.Infrastructure;

public sealed class EasyPostOptions
{
    public const string SectionName = "EasyPost";
    public string ApiKey { get; set; } = string.Empty;
    public bool TestMode { get; set; } = true;
    public string WebhookSecret { get; set; } = string.Empty;
}

/// <summary>Thrown when a rate id sent by the caller no longer matches any
/// rate on the EasyPost shipment (tampered/stale id). Lets the controller
/// return an explicit 400 instead of silently substituting another rate.</summary>
public sealed class RateNotFoundException(string rateId) : Exception($"Rate '{rateId}' not found on shipment.");

public sealed class EasyPostShippingProvider : IShippingProvider
{
    private readonly IOptionsMonitor<EasyPostOptions> _optionsMonitor;
    private readonly ILogger<EasyPostShippingProvider> _logger;
    private readonly ResiliencePipeline _resilience;

    public EasyPostShippingProvider(IOptionsMonitor<EasyPostOptions> optionsMonitor, ILogger<EasyPostShippingProvider> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _resilience = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(200),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
            })
            .Build();
    }

    private Client CreateClient() => new(new ClientConfiguration(_optionsMonitor.CurrentValue.ApiKey));

    public async Task<CreateShipmentResult> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct)
    {
        var client = CreateClient();
        var shipment = await _resilience.ExecuteAsync(async token => await client.Shipment.Create(new EasyPost.Parameters.Shipment.Create
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
        }, token), ct);

        var rates = shipment.Rates?.Select(r => new RateDto(
            r.Id ?? "unknown",
            r.Carrier ?? "unknown",
            r.Service ?? "standard",
            decimal.TryParse(r.Price, out var amt) ? amt : 0m,
            r.Currency ?? string.Empty,
            r.DeliveryDays
        )).ToList() ?? [];

        _logger.LogInformation("Created EasyPost shipment {ShipmentId} with {RateCount} rates",
            shipment.Id, rates.Count);

        return new CreateShipmentResult(shipment.Id ?? "", rates);
    }

    public async Task<BuyLabelResult> BuyLabelAsync(string shipmentId, string rateId, CancellationToken ct)
    {
        var client = CreateClient();
        var shipment = await _resilience.ExecuteAsync(async token => await client.Shipment.Retrieve(shipmentId, token), ct);
        var rate = shipment.Rates?.FirstOrDefault(r => string.Equals(r.Id, rateId, StringComparison.Ordinal));
        if (rate == null)
            throw new RateNotFoundException(rateId);

        var bought = await _resilience.ExecuteAsync(
            async token => await client.Shipment.Buy(shipmentId, rate.Id, insuranceValue: null, endShipperId: null, token), ct);

        return new BuyLabelResult(
            bought.TrackingCode ?? "",
            bought.Tracker?.PublicUrl ?? "",
            bought.PostageLabel?.LabelUrl ?? "",
            rate.Carrier ?? "unknown",
            rate.Service ?? "standard",
            decimal.TryParse(rate.Price, out var amt) ? amt : 0m,
            rate.Currency ?? string.Empty,
            bought.Tracker?.EstDeliveryDate);
    }

    public bool ValidateWebhookSignature(byte[] rawBody, IDictionary<string, string> headers)
    {
        var secret = _optionsMonitor.CurrentValue.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("EasyPost webhook secret not configured; rejecting webhook");
            return false;
        }

        var client = CreateClient();
        var headerObjects = headers.ToDictionary(h => h.Key, object? (h) => h.Value);
        try
        {
            client.Webhook.ValidateWebhook(rawBody, headerObjects, secret);
            return true;
        }
        catch (SignatureVerificationError)
        {
            return false;
        }
    }

    public async Task<TrackingInfo> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        var client = CreateClient();
        var tracker = await _resilience.ExecuteAsync(async token => await client.Tracker.Create(new EasyPost.Parameters.Tracker.Create
        {
            TrackingCode = trackingNumber,
        }, token), ct);

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
}
