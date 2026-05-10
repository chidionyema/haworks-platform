using MediatR;
using Microsoft.AspNetCore.Mvc;
using Haworks.Pricing.Application.Commands;
using Haworks.Pricing.Application.Queries;
using Haworks.BuildingBlocks.Common;

namespace Haworks.Pricing.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PromotionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 20, CancellationToken ct = default)
        => (await mediator.Send(new ListPromotionsQuery(skip, take), ct)).ToActionResult();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePromotionCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToCreatedActionResult(nameof(List), new { });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => (await mediator.Send(new DeletePromotionCommand(id), ct)).ToNoContentActionResult();
}
