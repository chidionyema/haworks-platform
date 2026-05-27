using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Haworks.Payments.Application.Commands.Subscriptions;
using Haworks.Payments.Application.Queries.Subscriptions;
using Haworks.BuildingBlocks.Extensions;

namespace Haworks.Payments.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/subscriptions")]
[Authorize]
public sealed class SubscriptionsController(IMediator mediator) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await mediator.Send(new GetSubscriptionStatusQuery(userId), ct);
        return result.ToActionResult();
    }

    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession(
        [FromBody] CreateSubscriptionCheckoutRequest body, CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var idempotencyKey = body.IdempotencyKey ?? DeriveIdempotencyKey(userId, "create-checkout", body.PriceId);
        var command = new CreateSubscriptionCheckoutCommand(
            userId,
            body.PriceId,
            body.Amount,
            body.RedirectPath,
            idempotencyKey);

        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(
        [FromBody] CancelSubscriptionRequest body, CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var idempotencyKey = body.IdempotencyKey ?? DeriveIdempotencyKey(userId, "cancel", body.SubscriptionId);
        var result = await mediator.Send(new CancelSubscriptionCommand(userId, body.SubscriptionId, idempotencyKey, body.Immediate), ct);
        return result.ToActionResult();
    }

    [HttpPost("resume")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(
        [FromBody] ResumeSubscriptionRequest body, CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var idempotencyKey = body.IdempotencyKey ?? DeriveIdempotencyKey(userId, "resume", body.SubscriptionId);
        var result = await mediator.Send(new ResumeSubscriptionCommand(userId, body.SubscriptionId, idempotencyKey), ct);
        return result.ToActionResult();
    }

    private static string DeriveIdempotencyKey(string userId, string operation, string subscriptionId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"subscription:{userId}:{operation}:{subscriptionId}"));
        var guid = new Guid(hash[..16]);
        return guid.ToString("N");
    }
}

public sealed record CreateSubscriptionCheckoutRequest
{
    public required string PriceId { get; init; }
    public required decimal Amount { get; init; }
    public string? RedirectPath { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record CancelSubscriptionRequest(string SubscriptionId, bool Immediate = false, string? IdempotencyKey = null);
public sealed record ResumeSubscriptionRequest(string SubscriptionId, string? IdempotencyKey = null);
