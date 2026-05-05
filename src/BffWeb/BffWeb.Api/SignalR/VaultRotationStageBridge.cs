using Haworks.BffWeb.Application.Interfaces;
using Haworks.Contracts.Identity;
using MassTransit;

namespace Haworks.BffWeb.Api.SignalR;

public sealed class VaultRotationStageBridge(IDemoHubNotifier notifier, ILogger<VaultRotationStageBridge> logger)
    : IConsumer<VaultRotationStageEvent>
{
    public Task Consume(ConsumeContext<VaultRotationStageEvent> ctx)
    {
        logger.LogInformation("Bridging VaultRotationStageEvent ({Stage}) -> OnVaultRotation for session {SessionId}", ctx.Message.Stage, ctx.Message.SessionId);
        
        // The frontend expects the stages: started, activated, grace_period, revoked
        // Map our more granular backend stages to what the frontend expects.
        string frontendStage = ctx.Message.Stage switch
        {
            "started" => "started",
            "credentials-fetched" => "activated",
            "applied" => "activated",
            "validated" => "grace_period",
            "revoked-old" => "revoked",
            _ => "started"
        };

        return notifier.NotifyVaultRotationAsync(new VaultRotationEvent(
            SessionId: ctx.Message.SessionId,
            Stage: frontendStage,
            Version: ctx.Message.NewVersion,
            PreviousVersion: ctx.Message.PreviousVersion,
            Timestamp: ctx.Message.Timestamp), ctx.CancellationToken);
    }
}
