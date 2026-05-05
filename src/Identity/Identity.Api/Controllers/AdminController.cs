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
    IVaultService vault,
    Haworks.BuildingBlocks.Messaging.IDomainEventPublisher publisher,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Forces a Vault credential refresh for a named AppRole and emits
    /// per-stage events through the EF outbox so BffWeb's
    /// <c>VaultRotationStageBridge</c> can stream the lifecycle into the
    /// portfolio's vault-rotation demo SignalR stream.
    ///
    /// <see cref="IVaultService"/> is registered via
    /// <c>services.AddVaultIntegration(...)</c> in
    /// <c>Identity.Infrastructure.DependencyInjection</c>.
    /// </summary>
    [HttpPost("vault/rotate-credentials")]
    public IActionResult RotateCredentials(
        // Default to identity's real Vault dynamic-Postgres role (per
        // deploy/aspire/manifests/database/roles.json). Calling
        // RefreshCredentials issues a fresh ephemeral DB user under this
        // role — that's the meaningful "rotation" for the demo.
        [FromQuery] string roleName = "haworks-identity",
        [FromQuery] Guid? sessionId = null)
    {
        var resolvedSession = sessionId ?? Guid.NewGuid();

        // Fire-and-forget: the actual Vault round-trip can take several
        // hundred ms, but the demo wants the HTTP response immediately so
        // the frontend can subscribe to the SignalR stream of stage events
        // without holding the request open.
        _ = Task.Run(async () =>
        {
            const int newVersion = 1;
            const string previousVersion = "current";
            try
            {
                await PublishStageAsync(resolvedSession, "started", newVersion, previousVersion);

                // The single stage that's bound to a real Vault round-trip
                // today. The IVaultService cycles the AppRole-backed
                // credential store under this role — RefreshCredentials
                // re-issues the dynamic Postgres lease tied to it.
                // Lazy-init: VaultService requires InitializeAsync to be
                // called once before its first use; idempotent so safe to
                // call here on every rotate.
                await vault.InitializeAsync();
                await vault.RefreshCredentials(roleName);
                await PublishStageAsync(resolvedSession, "credentials-fetched", newVersion, previousVersion);

                // 'applied' / 'validated' / 'revoked-old' aren't surfaced as
                // distinct hooks on IVaultService today — these publishes
                // are real broker round-trips but their semantic is
                // best-effort. Adding IProgress<VaultStage> to
                // IVaultService.RefreshCredentials would let each stage
                // correspond to a real Vault sub-operation; tracked
                // separately.
                await PublishStageAsync(resolvedSession, "applied", newVersion, previousVersion);
                await PublishStageAsync(resolvedSession, "validated", newVersion, previousVersion);
                await PublishStageAsync(resolvedSession, "revoked-old", newVersion, previousVersion);

                logger.LogInformation("Vault credentials refreshed for role={RoleName}", roleName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vault rotation failed for role={RoleName}", roleName);
            }
        });

        return Accepted(new { roleName, status = "Rotating", sessionId = resolvedSession });
    }

    private Task PublishStageAsync(Guid sessionId, string stage, int newVersion, string previousVersion) =>
        publisher.PublishAsync(new Haworks.Contracts.Identity.VaultRotationStageEvent
        {
            SessionId = sessionId,
            Stage = stage,
            NewVersion = newVersion,
            PreviousVersion = previousVersion,
            Timestamp = DateTime.UtcNow,
        });
}
