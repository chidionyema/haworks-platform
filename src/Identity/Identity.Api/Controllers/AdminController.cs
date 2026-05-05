using Haworks.BuildingBlocks.Vault;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Haworks.Identity.Api.Controllers;

/// <summary>
/// Operational endpoints for identity-svc — exposed for the portfolio
/// site's demo flow via BffWeb. NOT part of identity's user-facing
/// surface; in production these MUST be locked behind a localhost-only
/// or mesh-only middleware (TODO: layer guard before prod deploy).
///
/// AllowAnonymous + minimal — same pattern as Catalog.Api/DemoTestController.
/// </summary>
[ApiController]
[Route("admin")]
[AllowAnonymous]
public sealed class AdminController(
    IServiceProvider services,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Forces a Vault credential refresh for a named AppRole. T2.4's
    /// vault-rotation demo posts here. If <see cref="IVaultService"/> isn't
    /// registered (Vault integration not wired into this identity-svc
    /// instance), logs a warning and returns 202 anyway — keeps the demo
    /// HTTP contract honest while not pretending to rotate something that
    /// doesn't exist.
    /// </summary>
    [HttpPost("vault/rotate-credentials")]
    public IActionResult RotateCredentials([FromQuery] string roleName = "identity-jwt", [FromQuery] Guid? sessionId = null)
    {
        var vault = services.GetService<IVaultService>();
        var publisher = services.GetService<Haworks.BuildingBlocks.Messaging.IDomainEventPublisher>();
        var resolvedSession = sessionId ?? Guid.NewGuid();

        if (vault is null)
        {
            logger.LogWarning(
                "Vault rotate requested for role={RoleName} but IVaultService is not registered.", roleName);
            return Accepted(new
            {
                roleName,
                status = "AcceptedNoVault",
                message = "Demo endpoint reached; vault integration not registered.",
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Fake version numbers for the demo since VaultService doesn't expose them
                var previousVersion = "v1";
                var newVersion = 2;

                if (publisher != null) await publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent { SessionId = resolvedSession, Stage = "started", NewVersion = newVersion, PreviousVersion = previousVersion, Timestamp = DateTime.UtcNow });
                
                await vault.RefreshCredentials(roleName);
                if (publisher != null) await publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent { SessionId = resolvedSession, Stage = "credentials-fetched", NewVersion = newVersion, PreviousVersion = previousVersion, Timestamp = DateTime.UtcNow });
                
                // Simulate the other stages for the demo visual
                await Task.Delay(100);
                if (publisher != null) await publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent { SessionId = resolvedSession, Stage = "applied", NewVersion = newVersion, PreviousVersion = previousVersion, Timestamp = DateTime.UtcNow });
                
                await Task.Delay(100);
                if (publisher != null) await publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent { SessionId = resolvedSession, Stage = "validated", NewVersion = newVersion, PreviousVersion = previousVersion, Timestamp = DateTime.UtcNow });
                
                await Task.Delay(100);
                if (publisher != null) await publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent { SessionId = resolvedSession, Stage = "revoked-old", NewVersion = newVersion, PreviousVersion = previousVersion, Timestamp = DateTime.UtcNow });

                logger.LogInformation("Vault credentials refreshed for role={RoleName}", roleName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vault rotation failed for role={RoleName}", roleName);
            }
        });

        return Accepted(new { roleName, status = "Rotating", sessionId = resolvedSession });
    }
}
