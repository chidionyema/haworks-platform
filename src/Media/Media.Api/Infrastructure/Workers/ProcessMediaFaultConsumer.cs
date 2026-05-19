using Haworks.Contracts.Media;
using MassTransit;

namespace Haworks.Media.Api.Infrastructure.Workers;

/// <summary>
/// Handles <see cref="Fault{ProcessMediaCommand}"/> after all retries are exhausted.
/// Publishes a definitive <see cref="MediaProcessingFailedEvent"/> so downstream
/// services know the file is clean but processing didn't complete.
/// </summary>
public sealed class ProcessMediaFaultConsumer(
    ILogger<ProcessMediaFaultConsumer> logger) : IConsumer<Fault<ProcessMediaCommand>>
{
    public Task Consume(ConsumeContext<Fault<ProcessMediaCommand>> context)
    {
        var cmd = context.Message.Message;
        var reason = context.Message.Exceptions?.FirstOrDefault()?.Message
            ?? "Unknown processing error after retries exhausted.";

        logger.LogError("Media processing permanently failed for {MediaId}: {Reason}", cmd.MediaId, reason);

        return context.Publish(new MediaProcessingFailedEvent
        {
            MediaId = cmd.MediaId,
            OwnerId = cmd.OwnerId,
            FileName = cmd.FileName,
            Reason = $"Media processing failed after retries: {reason}",
        }, context.CancellationToken);
    }
}
