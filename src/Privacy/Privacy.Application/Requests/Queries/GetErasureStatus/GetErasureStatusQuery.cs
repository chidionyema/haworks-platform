using Haworks.Privacy.Application.Common.Interfaces;
using Haworks.Privacy.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;

public record GetErasureStatusQuery(Guid RequestId, Guid UserId) : IRequest<ErasureStatusDto?>;

public record ErasureStatusDto(
    Guid RequestId,
    Guid UserId,
    PrivacyRequestType Type,
    PrivacyRequestStatus Status,
    DateTime CreatedAt,
    DateTimeOffset? CompletedAt);

public class GetErasureStatusQueryHandler : IRequestHandler<GetErasureStatusQuery, ErasureStatusDto?>
{
    private readonly IPrivacyDbContext _context;

    public GetErasureStatusQueryHandler(IPrivacyDbContext context)
    {
        _context = context;
    }

    public async Task<ErasureStatusDto?> Handle(GetErasureStatusQuery request, CancellationToken cancellationToken)
    {
        var record = await _context.PrivacyRequests
            .AsNoTracking()
            .Where(r => r.Id == request.RequestId && r.UserId == request.UserId)
            .Select(r => new ErasureStatusDto(r.Id, r.UserId, r.Type, r.Status, r.CreatedAt, r.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return record;
    }
}
