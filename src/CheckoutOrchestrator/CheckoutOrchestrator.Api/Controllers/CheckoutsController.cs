using Haworks.CheckoutOrchestrator.Application.Commands;
using Haworks.CheckoutOrchestrator.Application.Queries;
using Haworks.CheckoutOrchestrator.Api.Models;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Extensions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Haworks.CheckoutOrchestrator.Api.Controllers;

/// <summary>
/// REST surface for the saga.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Roles = "Admin,Service")]
public sealed class CheckoutsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Start([FromBody] StartCheckoutRequest body, CancellationToken ct)
    {
        var result = await mediator.Send(new StartCheckoutCommand(
            body.SagaId,
            body.OrderId,
            body.UserId,
            body.CustomerEmail,
            (long)Math.Round(body.TotalAmount * 100m, 0, MidpointRounding.AwayFromZero),
            body.IdempotencyKey,
            body.Items,
            body.Currency
        ), ct);

        if (!result.IsSuccess)
            return result.ToActionResult();

        return Accepted(new { sagaId = result.Value.SagaId, orderId = result.Value.OrderId });
    }

    [HttpGet("{sagaId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(Guid sagaId, CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Service");
        var result = await mediator.Send(new GetCheckoutSagaQuery(sagaId, userId, isAdmin), ct);
        return result.ToActionResult();
    }

    [HttpGet("by-order/{orderId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByOrderId(Guid orderId, CancellationToken ct)
    {
        var userId = HttpContext.GetForwardedUserId();
        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Service");
        var result = await mediator.Send(new GetCheckoutSagaByOrderIdQuery(orderId, userId, isAdmin), ct);
        return result.ToActionResult();
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? state,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new ListCheckoutSagasQuery(state, from, to, limit, offset), ct);
        return result.ToActionResult();
    }

    [HttpGet("{sagaId:guid}/audit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudit(Guid sagaId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetCheckoutSagaAuditQuery(sagaId), ct);
        return result.ToActionResult();
    }
}
