using Haworks.Privacy.Application.Requests.Commands.InitiateRequest;
using Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;
using Haworks.Privacy.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Haworks.Privacy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("api")]
public class PrivacyRequestsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PrivacyRequestsController> _logger;

    public PrivacyRequestsController(IMediator mediator, ILogger<PrivacyRequestsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Initiate(InitiatePrivacyRequestCommand command, CancellationToken ct = default)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        // Service-to-service calls (sub=bff-service) use the UserId from the request body.
        // SECURITY NOTE: The "Service" role is necessary but not sufficient — we also require
        // the privacy:erase scope/claim to reduce the blast radius if a service token is
        // compromised. If claim infrastructure is not yet available, this check is documented
        // here as a required control and the initiating service identity is audit-logged.
        // TODO: enforce privacy:erase claim once claim infrastructure is provisioned (PLATFORM-SEC-12).
        if (User.IsInRole("Service") && command.UserId != Guid.Empty)
        {
            var initiatingService = User.FindFirst("sub")?.Value ?? "unknown-service";
            var hasEraseScope = User.HasClaim("scope", "privacy:erase")
                             || User.HasClaim("privacy:erase", "true");

            if (!hasEraseScope)
            {
                // Audit-log the missing claim so the security team can track callers
                // that haven't yet been updated to include the privacy:erase scope.
                _logger.LogWarning(
                    "IDOR risk: privacy erasure initiated by service {InitiatingService} without privacy:erase scope. " +
                    "UserId={UserId}. Proceeding (enforcement pending PLATFORM-SEC-12).",
                    initiatingService, command.UserId);
            }
            else
            {
                _logger.LogInformation(
                    "Privacy erasure initiated by service {InitiatingService} with privacy:erase scope. UserId={UserId}",
                    initiatingService, command.UserId);
            }

            var id = await _mediator.Send(command, ct);
            return Ok(new { RequestId = id });
        }

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var secureCommand = command with { UserId = userId };
        var result = await _mediator.Send(secureCommand, ct);
        return Ok(new { RequestId = result });
    }

    /// <summary>
    /// Returns the current status of a privacy erasure request.
    /// Users can only query their own requests (enforced by user ID from JWT).
    /// </summary>
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatus(Guid requestId, CancellationToken ct = default)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var result = await _mediator.Send(new GetErasureStatusQuery(requestId, userId), ct);
        if (result is null)
            return NotFound();

        return Ok(result);
    }
}
