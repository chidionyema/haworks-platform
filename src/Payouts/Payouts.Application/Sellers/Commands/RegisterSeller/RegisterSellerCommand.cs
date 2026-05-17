using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Domain.Aggregates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Payouts.Application.Sellers.Commands.RegisterSeller;

public record RegisterSellerCommand(Guid SellerId, string Email, string IdempotencyKey = "") : IRequest<Guid>;

public class RegisterSellerCommandHandler : IRequestHandler<RegisterSellerCommand, Guid>
{
    private readonly IPayoutsDbContext _context;
    private readonly IPayoutGateway _payoutGateway;

    public RegisterSellerCommandHandler(IPayoutsDbContext context, IPayoutGateway payoutGateway)
    {
        _context = context;
        _payoutGateway = payoutGateway;
    }

    public async Task<Guid> Handle(RegisterSellerCommand request, CancellationToken cancellationToken)
    {
        // H1 Fix: Check existence BEFORE calling Stripe to avoid orphaning billable Connect accounts
        var existing = await _context.SellerProfiles
            .FirstOrDefaultAsync(p => p.SellerId == request.SellerId, cancellationToken);
        if (existing != null) return existing.Id;

        var externalId = await _payoutGateway.CreateConnectedAccountAsync(request.SellerId, request.Email);

        var profile = SellerProfile.Create(request.SellerId);
        profile.ExternalProviderId = externalId;
        _context.SellerProfiles.Add(profile);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Race: another thread inserted between our check and save.
            // The Stripe account is now orphaned — clean it up.
            await _payoutGateway.DeleteConnectedAccountAsync(externalId);

            var raceWinner = await _context.SellerProfiles
                .FirstOrDefaultAsync(p => p.SellerId == request.SellerId, cancellationToken);
            if (raceWinner != null) return raceWinner.Id;
            throw;
        }

        return profile.Id;
    }
}
