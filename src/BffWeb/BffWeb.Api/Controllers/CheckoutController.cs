using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using Haworks.BffWeb.Application.Telemetry;
using Haworks.BuildingBlocks.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Haworks.BffWeb.Api.Controllers;

/// <summary>
/// The public checkout flow:
///   1. Browser POSTs /api/checkout with cart items.
///   2. bff-web allocates a sagaId + orderId, forwards to checkout-svc'
///      <c>POST /api/checkouts</c>.
///   3. Returns {sagaId, orderId} immediately.
///   4. Browser opens a SignalR connection to /hubs/checkout and
///      subscribes to its sagaId.
///   5. The CheckoutSaga publishes StockReservationRequested →
///      catalog reserves → publishes StockReserved → CheckoutSaga
///      publishes PaymentSessionRequested → payments creates session
///      → publishes PaymentSessionCreated → bff-web's
///      PaymentSessionCreatedConsumer pushes the URL via SignalR.
///   6. Browser navigates to the checkout URL.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public sealed class CheckoutController(
    IHttpClientFactory httpClientFactory,
    Haworks.BuildingBlocks.Authentication.IServiceTokenProvider serviceTokenProvider,
    ILogger<CheckoutController> logger) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("expensive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Start([FromBody] CheckoutRequest body, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // User-scoped idempotency key. Every retry of the *same* checkout from the
        // *same* user collapses to the same key; a different user's replay with
        // the same client-supplied nonce can't collide because UserId is in the
        // hash. Per .claude/rules/dotnet-clean-arch.md "MANDATORY: SHA-256
        // Idempotency".
        var cartShape = string.Join(
            ",",
            body.Items
                .OrderBy(i => i.ProductId)
                .Select(i => $"{i.ProductId}:{i.Quantity}:{i.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)}"));
        var idempotencyKey = IdempotencyKey.Derive(
            userId: userId,
            operation: "checkout.start",
            body.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
            cartShape,
            body.IdempotencyKey ?? string.Empty);

        // Derive deterministic IDs from idempotency key to prevent duplicate sagas on retry
        var sagaId = DeriveGuidFromString(idempotencyKey + ".saga");
        var orderId = DeriveGuidFromString(idempotencyKey + ".order");

        using var activity = BffWebActivities.Source.StartActivity("bff.checkout.start");
        activity?.SetTag("saga.id", sagaId);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("customer.id", userId);
        activity?.SetTag("checkout.total_amount_cents", (long)Math.Round(body.TotalAmount * 100m, 0, MidpointRounding.AwayFromZero));
        activity?.SetTag("checkout.item_count", body.Items.Count);

        var client = httpClientFactory.CreateClient(BackendClients.Checkout);

        // Checkout-svc requires Roles=Service. The UserIdentityForwardingHandler
        // forwards the user JWT which lacks this role. Override with service token.
        var svcToken = await serviceTokenProvider.GetTokenAsync(ct);
        if (string.IsNullOrEmpty(svcToken))
        {
            return StatusCode(503, new { error = "Service authentication unavailable" });
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkouts");
        req.Content = JsonContent.Create(new
        {
            sagaId,
            orderId,
            userId = userId,
            customerEmail = body.CustomerEmail,
            totalAmount = body.TotalAmount,
            idempotencyKey,
            items = body.Items.Select(i => new
            {
                productId = i.ProductId,
                productName = i.ProductName,
                quantity = i.Quantity,
                unitPriceCents = (long)Math.Round(i.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero),
                currency = body.Currency ?? "USD",
            }),
        });
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", svcToken);
        req.Headers.TryAddWithoutValidation("X-User-Id", userId);
        var resp = await client.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var payload = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "checkout-svc rejected saga start: {StatusCode} {Body}", resp.StatusCode, payload);
            return StatusCode((int)resp.StatusCode, new { error = "checkout-svc rejected the request", detail = payload });
        }

        return Accepted(new { sagaId, orderId, message = "subscribe to /hubs/checkout to receive the payment URL" });
    }

    private static Guid DeriveGuidFromString(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        // Set version (4) and variant bits for UUID v4 compatibility
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

public sealed record CheckoutRequest
{
    [Required]
    [EmailAddress]
    public required string CustomerEmail { get; init; }

    [Required]
    [Range(0.01, 999_999.99)]
    public required decimal TotalAmount { get; init; }

    [Required]
    [RegularExpression("^[A-Z]{3}$")]
    public string Currency { get; init; } = "USD";

    public string? IdempotencyKey { get; init; }

    [Required]
    [MinLength(1)]
    public required IReadOnlyList<CheckoutLineItem> Items { get; init; }
}

public sealed record CheckoutLineItem
{
    [Required]
    public required Guid ProductId { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string ProductName { get; init; }

    [Required]
    [Range(1, 1000)]
    public required int Quantity { get; init; }

    [Required]
    [Range(0.01, 9999.99)]
    public required decimal UnitPrice { get; init; }
}
