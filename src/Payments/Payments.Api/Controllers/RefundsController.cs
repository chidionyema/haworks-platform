using Haworks.Payments.Application.Commands.Refunds;
using Haworks.BuildingBlocks.Extensions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Haworks.Payments.Api.Controllers;

[ApiController]
[Route("api/refunds")]
[Authorize] // Ideally restricted to Admin roles
public sealed class RefundsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRefundRequest body, 
        CancellationToken ct)
    {
        var command = new CreateRefundCommand(
            body.TransactionId,
            body.AmountCents,
            body.Currency,
            body.Reason,
            body.IdempotencyKey);

        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }
}

public sealed record CreateRefundRequest(
    string TransactionId,
    long? AmountCents = null,
    string? Currency = null,
    string? Reason = null,
    string? IdempotencyKey = null);
