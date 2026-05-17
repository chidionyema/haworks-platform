using Haworks.Pricing.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Haworks.Pricing.Application.Commands;

/// <summary>
/// Handles atomic promotion code redemption via repository CAS.
/// </summary>
public sealed class RedeemPromotionCodeCommandHandler : IRequestHandler<RedeemPromotionCodeCommand, RedeemPromotionCodeResult>
{
    private readonly IPromotionCodeRepository _repository;
    private readonly ILogger<RedeemPromotionCodeCommandHandler> _logger;

    public RedeemPromotionCodeCommandHandler(
        IPromotionCodeRepository repository,
        ILogger<RedeemPromotionCodeCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<RedeemPromotionCodeResult> Handle(RedeemPromotionCodeCommand request, CancellationToken ct)
    {
        var code = await _repository.GetByCodeAsync(request.Code.ToUpperInvariant(), ct).ConfigureAwait(false);
        if (code is null)
        {
            return new RedeemPromotionCodeResult { Success = false, FailureReason = "invalid" };
        }

        var now = DateTimeOffset.UtcNow;
        if (!code.CanRedeem(now))
        {
            var reason = code.ExpiresAt.HasValue && now >= code.ExpiresAt.Value ? "expired" : "inactive";
            if (code.MaxUses.HasValue && code.UsesCount >= code.MaxUses.Value) reason = "exhausted";
            return new RedeemPromotionCodeResult { Success = false, FailureReason = reason };
        }

        // C2 Fix: Per-user limit is now enforced INSIDE TryRedeemAsync (atomic with FOR UPDATE lock)
        // to prevent TOCTOU race where two concurrent requests both pass the check.
        var redeemed = await _repository.TryRedeemAsync(
            code.Id, request.OrderId, request.UserId, request.DiscountAmount,
            code.MaxUsesPerUser, ct).ConfigureAwait(false);

        if (!redeemed)
        {
            _logger.LogWarning("Promotion code {Code} redemption failed (CAS) for order {OrderId}", request.Code, request.OrderId);
            return new RedeemPromotionCodeResult { Success = false, FailureReason = "exhausted" };
        }

        _logger.LogInformation("Promotion code {Code} redeemed for order {OrderId}", request.Code, request.OrderId);
        return new RedeemPromotionCodeResult { Success = true };
    }
}
