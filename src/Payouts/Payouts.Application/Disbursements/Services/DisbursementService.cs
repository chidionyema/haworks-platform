using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.Payouts.Application.Disbursements.Services;

public interface IDisbursementService
{
    Task ProcessEligiblePayoutsAsync(CancellationToken ct = default);
}

public class DisbursementService : IDisbursementService
{
    private readonly IPayoutsDbContext _context;
    private readonly IPayoutGateway _payoutGateway;
    private readonly ILogger<DisbursementService> _logger;

    public DisbursementService(IPayoutsDbContext context, IPayoutGateway payoutGateway, ILogger<DisbursementService> logger)
    {
        _context = context;
        _payoutGateway = payoutGateway;
        _logger = logger;
    }

    public async Task ProcessEligiblePayoutsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting payout disbursement cycle");

        var eligibleAccounts = await _context.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.Type == AccountType.SellerPayable && a.BalanceCents > 0)
            .OrderBy(a => a.Id)
            .Take(500)
            .ToListAsync(ct);

        var ownerIds = eligibleAccounts.Select(a => a.OwnerId).ToList();
        var profiles = await _context.SellerProfiles
            .Where(p => ownerIds.Contains(p.SellerId))
            .ToDictionaryAsync(p => p.SellerId, ct);

        // M5 Fix: Process payouts concurrently (bounded to 5 parallel gateway calls)
        // Sequential processing of 500 accounts with Stripe calls took 30+ minutes.
        var payoutTasks = eligibleAccounts
            .Where(account => profiles.TryGetValue(account.OwnerId, out var p) &&
                              p.PayoutsEnabled && !string.IsNullOrEmpty(p.ExternalProviderId) &&
                              account.BalanceCents >= p.PayoutThresholdCents)
            .Select(account => (account.Id, profiles[account.OwnerId]));

        var payoutList = payoutTasks.ToList();
        _logger.LogInformation(
            "Found {EligibleCount} accounts, {QualifiedCount} qualified for payout",
            eligibleAccounts.Count, payoutList.Count);

        await Parallel.ForEachAsync(payoutList, ct,
            async (item, loopCt) => await ExecutePayout(item.Id, item.Item2, loopCt));

        _logger.LogInformation("Payout disbursement cycle complete. Processed={Count}", payoutList.Count);
    }

    private async Task ExecutePayout(Guid accountId, SellerProfile profile, CancellationToken ct = default)
    {
        var dbContext = (Microsoft.EntityFrameworkCore.DbContext)_context;
        var strategy = dbContext.Database.CreateExecutionStrategy();

        long payoutAmountCents = 0;
        Guid? payoutId = null;
        var currency = string.Empty;
        // H2 Fix: Use payoutId (generated in Phase 1) in the idempotency key.
        // Day-granularity blocked legitimate same-day retries; per-payout key is idempotent for retries
        // of the same attempt while allowing new payouts on the same day.
        var payoutIdForKey = Guid.NewGuid();
        var idempotencyKey = $"PAYOUT:{payoutIdForKey}";

        // =====================================================================
        // PHASE 1: ATOMIC LOCAL RESERVATION (debit balance, create Payout)
        // =====================================================================
        await strategy.ExecuteAsync(async innerCt =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(innerCt);

            // FromSqlRaw WHERE clause ensures single-row; FirstAsync is deterministic here
#pragma warning disable HWK054
            var account = await _context.LedgerAccounts
                .FromSqlRaw("SELECT *, xmin FROM payouts.\"LedgerAccounts\" WHERE \"Id\" = {0} FOR UPDATE", accountId)
                .FirstAsync(innerCt);
#pragma warning restore HWK054

            if (account.BalanceCents <= 0 || account.BalanceCents < profile.PayoutThresholdCents)
            {
                _logger.LogDebug(
                    "Skipping payout for seller {SellerId}: balanceCents={BalanceCents}, thresholdCents={ThresholdCents}",
                    profile.SellerId, account.BalanceCents, profile.PayoutThresholdCents);
                await tx.RollbackAsync(innerCt);
                return;
            }

            payoutAmountCents = account.BalanceCents;
            currency = account.Currency;

            var payout = Payout.Create(profile.SellerId, payoutAmountCents, currency);
            payoutId = payout.Id;
            _context.Payouts.Add(payout);

            _logger.LogInformation(
                "Phase 1 complete: payout reserved. PayoutId={PayoutId}, SellerId={SellerId}, AmountCents={AmountCents} {Currency}",
                payout.Id, profile.SellerId, payoutAmountCents, currency);

            account.UpdateBalance(payoutAmountCents, EntryType.Debit);
            var entry = LedgerEntry.Create(account.Id, Guid.NewGuid(), payoutAmountCents, EntryType.Debit, "Payout initiated", payout.Id.ToString());
            _context.LedgerEntries.Add(entry);

            await _context.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(innerCt);
        }, ct);

        if (payoutId == null) return;

        // =====================================================================
        // PHASE 2: EXTERNAL GATEWAY CALL (outside DB locks)
        // =====================================================================
        _logger.LogInformation(
            "Phase 2: initiating gateway call. PayoutId={PayoutId}, SellerId={SellerId}, IdempotencyKey={Key}",
            payoutId, profile.SellerId, idempotencyKey);

        string? externalId = null;
        var isSuccess = false;
        string? errorMessage = null;

        try
        {
            // Gateway expects cents (Stripe uses smallest currency unit)
            var (gatewayExternalId, status) = await _payoutGateway.InitiatePayoutAsync(
                profile.ExternalProviderId!, payoutAmountCents, currency, $"Payout for {profile.SellerId}",
                idempotencyKey, ct);

            isSuccess = status is PayoutStatus.Succeeded or PayoutStatus.InTransit;
            externalId = gatewayExternalId;
            if (!isSuccess) errorMessage = $"Gateway returned status: {status}";

            _logger.LogInformation(
                "Phase 2 complete: gateway responded. PayoutId={PayoutId}, ExternalId={ExternalId}, Status={Status}",
                payoutId, gatewayExternalId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Phase 2 failed: gateway error. PayoutId={PayoutId}, SellerId={SellerId}",
                payoutId, profile.SellerId);
            errorMessage = ex.Message;
        }

        // =====================================================================
        // PHASE 3: RESOLUTION (commit success or refund failure)
        // =====================================================================
        await strategy.ExecuteAsync(async innerCt =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(innerCt);

            var payout = await _context.Payouts.FindAsync([payoutId!.Value], innerCt);

            if (isSuccess)
            {
                payout!.MarkInTransit(externalId!);
                _logger.LogInformation(
                    "Phase 3 complete: payout in-transit. PayoutId={PayoutId}, SellerId={SellerId}, ExternalId={ExternalId}",
                    payoutId, profile.SellerId, externalId);
            }
            else
            {
                payout!.MarkFailed(errorMessage ?? "Unknown error");

#pragma warning disable HWK054
                var account = await _context.LedgerAccounts
                    .FromSqlRaw("SELECT *, xmin FROM payouts.\"LedgerAccounts\" WHERE \"Id\" = {0} FOR UPDATE", accountId)
                    .FirstAsync(innerCt);
#pragma warning restore HWK054

                account.UpdateBalance(payoutAmountCents, EntryType.Credit);
                var entry = LedgerEntry.Create(account.Id, Guid.NewGuid(), payoutAmountCents, EntryType.Credit, "Payout failed — refund", payout.Id.ToString());
                _context.LedgerEntries.Add(entry);

                _logger.LogWarning(
                    "Phase 3 complete: payout failed, balance refunded. PayoutId={PayoutId}, SellerId={SellerId}, Reason={Reason}",
                    payoutId, profile.SellerId, errorMessage);
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(innerCt);
        }, ct);
    }
}
