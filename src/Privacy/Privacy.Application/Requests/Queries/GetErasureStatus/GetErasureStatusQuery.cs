using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Application.Requests.Sagas;
using Haworks.Privacy.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;

public record GetErasureStatusQuery(Guid RequestId, Guid UserId) : IRequest<ErasureStatusDto?>;

public record ErasureStatusDto(
    Guid RequestId,
    Guid UserId,
    PrivacyRequestType Type,
    PrivacyRequestStatus Status,
    DateTime CreatedAt,
    DateTimeOffset? CompletedAt,
    bool IdentityErased,
    bool OrdersErased,
    bool PaymentsErased,
    string? FailedServices);

// H11: query the saga state table so callers see real-time saga status instead of a
//      PrivacyRequest entity that is never updated after initiation.
public class GetErasureStatusQueryHandler : IRequestHandler<GetErasureStatusQuery, ErasureStatusDto?>
{
    private readonly ISagaStateRepository _sagaRepository;
    private readonly ILogger<GetErasureStatusQueryHandler> _logger;

    public GetErasureStatusQueryHandler(
        ISagaStateRepository sagaRepository,
        ILogger<GetErasureStatusQueryHandler> logger)
    {
        _sagaRepository = sagaRepository;
        _logger = logger;
    }

    public async Task<ErasureStatusDto?> Handle(GetErasureStatusQuery request, CancellationToken cancellationToken)
    {
        var saga = await _sagaRepository.FindAsync(request.RequestId, cancellationToken);

        if (saga is null)
        {
            _logger.LogDebug("No saga state found for request {RequestId}", request.RequestId);
            return null;
        }

        // The saga's UserId must match the authenticated caller (IDOR guard).
        if (saga.UserId != request.UserId)
        {
            _logger.LogWarning(
                "GetErasureStatus: requestId {RequestId} belongs to user {OwnerId}, not caller {CallerId}",
                request.RequestId, saga.UserId, request.UserId);
            return null;
        }

        var status = saga.CurrentState switch
        {
            "Processing" => PrivacyRequestStatus.InProgress,
            "Stalled"    => PrivacyRequestStatus.InProgress,
            "Completed"  => PrivacyRequestStatus.Completed,
            "Failed"     => PrivacyRequestStatus.Failed,
            _            => PrivacyRequestStatus.Pending
        };

        return new ErasureStatusDto(
            RequestId:      saga.CorrelationId,
            UserId:         saga.UserId,
            Type:           PrivacyRequestType.Erasure,
            Status:         status,
            CreatedAt:      saga.CreatedAt ?? DateTime.MinValue,
            CompletedAt:    saga.CompletedAt.HasValue ? new DateTimeOffset(saga.CompletedAt.Value, TimeSpan.Zero) : null,
            IdentityErased: saga.IdentityCompleted,
            OrdersErased:   saga.OrdersCompleted,
            PaymentsErased: saga.PaymentsCompleted,
            FailedServices: saga.FailedServices);
    }
}
