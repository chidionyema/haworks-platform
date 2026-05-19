using Haworks.Payments.Application.Commands.Refunds;
using Haworks.Payments.Application.Queries.Refunds;
using Haworks.BuildingBlocks.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Haworks.Payments.Api.Controllers;

[ApiController]
[Route("api/refunds")]
[Authorize(Roles = "Admin")]
public sealed class RefundsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRefundRequest body, 
        CancellationToken ct)
    {
        var command = new CreateRefundCommand(
            body.PaymentId,
            body.Amount,
            body.Currency,
            Guid.NewGuid().ToString("N"),
            body.Reason,
            body.RequestedBy);

        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetRefundSagaStateQuery(id), ct);
        return result.ToActionResult();
    }
}

public sealed record CreateRefundRequest
{
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public string? Reason { get; init; }
    public string? RequestedBy { get; init; }
}
