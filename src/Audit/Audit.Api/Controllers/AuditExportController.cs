using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Haworks.Audit.Application.Export;

namespace Haworks.Audit.Api.Controllers;

[ApiController]
[Route("audit/export")]
[Authorize]
public class AuditExportController : ControllerBase
{
    private readonly IAuditExportJob _exportService;
    private readonly IValidator<AuditExportRequest> _validator;

    public AuditExportController(IAuditExportJob exportService, IValidator<AuditExportRequest> validator)
    {
        _exportService = exportService;
        _validator = validator;
    }

    [HttpPost]
    [Authorize(Roles = "audit-admin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnqueueExport([FromBody] AuditExportRequest request, CancellationToken ct)
    {
        var validationResult = await _validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));

        var requestedBy = User.Identity?.Name ?? throw new UnauthorizedAccessException("User identity name required");
        var jobId = await _exportService.EnqueueAsync(request, requestedBy, ct);
        return Accepted(new { jobId, status = "queued" });
    }

    [HttpGet("{jobId:guid}")]
    [Authorize(Roles = "audit-reader")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExportStatus(Guid jobId, CancellationToken ct)
    {
        var snapshot = await _exportService.GetStatusAsync(jobId, ct);
        if (snapshot == null) return NotFound();
        return Ok(snapshot);
    }
}
