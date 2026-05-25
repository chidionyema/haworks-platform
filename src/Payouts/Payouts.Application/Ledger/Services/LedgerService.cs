using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.Payouts.Application.Ledger.Services;

public interface ILedgerService
{
    Task CreditSellerAsync(Guid sellerId, long amountCents, string currency, Guid referenceId, string description, CancellationToken ct = default);
    Task DebitSellerAsync(Guid sellerId, long amountCents, string currency, Guid referenceId, string description, CancellationToken ct = default);
    Task<long> GetBalanceAsync(Guid sellerId, AccountType type, string currency, CancellationToken ct = default);
    Task<bool> HasCreditForReferenceAsync(Guid referenceId, CancellationToken ct = default);
}

public class LedgerService : ILedgerService
{
    private readonly IPayoutsDbContext _context;
    private readonly ILogger<LedgerService> _logger;
    private static readonly Guid SystemPlatformId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // True when running under an in-memory or SQLite provider (unit tests) — FOR UPDATE is not supported.
    private readonly bool _useLinqFallback;

    public LedgerService(IPayoutsDbContext context, ILogger<LedgerService> logger)
    {
        _context = context;
        _logger = logger;
        _useLinqFallback = context is DbContext dbCtx &&
            (dbCtx.Database.ProviderName?.Contains("InMemory") == true ||
             dbCtx.Database.ProviderName?.Contains("Sqlite") == true);
    }

    /// <summary>
    /// Credits a seller's pending account after a payment completes.
    /// Double-entry: Credit PlatformHolding, Debit SellerPending + PlatformRevenue.
    ///
    /// Idempotent: checks if ReferenceId already has ledger entries before writing.
    ///
    /// IMPORTANT: When called from a MassTransit consumer, the ambient EF Outbox
    /// transaction provides atomicity. DO NOT open a nested transaction. The FOR UPDATE
    /// locks provide concurrency control within the ambient transaction.
    /// When called from HTTP/MediatR (no ambient tx), the caller is responsible for
    /// wrapping in a transaction if needed.
    /// </summary>
    public async Task CreditSellerAsync(Guid sellerId, long amountCents, string currency, Guid referenceId, string description, CancellationToken ct = default)
    {
        if (amountCents <= 0) throw new ArgumentException("Amount must be positive", nameof(amountCents));

        var alreadyProcessed = await _context.LedgerEntries
            .AnyAsync(e => e.ReferenceId == referenceId.ToString(), ct);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Ledger credit for reference {ReferenceId} already processed — skipping", referenceId);
            return;
        }

        var profile = await _context.SellerProfiles.Where(p => p.SellerId == sellerId).FirstOrDefaultAsync(ct);
        var commissionRate = profile?.CommissionPercentage ?? 10.00m;
        // Snapshot as basis points (10.00% = 1000 bps) for immutable audit trail
        var commissionRateBps = (int)(commissionRate * 100m);
        // Integer arithmetic: commission in cents, truncated toward zero (platform keeps remainder)
        var commissionCents = (long)(amountCents * commissionRate / 100m);
        var sellerAmountCents = amountCents - commissionCents;

        var transactionId = Guid.CreateVersion7();
        var sellerAccount = await GetOrCreateAccountWithLock(sellerId, AccountType.SellerPending, currency, ct);
        var platformHoldingAccount = await GetOrCreateAccountWithLock(SystemPlatformId, AccountType.PlatformHolding, currency, ct);
        var platformRevenueAccount = await GetOrCreateAccountWithLock(SystemPlatformId, AccountType.PlatformRevenue, currency, ct);

        // Double-entry bookkeeping:
        // All three balances increase when a payment arrives.
        // UpdateBalance uses Credit=add, Debit=subtract for running balances.
        // LedgerEntry.Type records the accounting classification:
        //   Debit PlatformHolding (asset increase) = amountCents
        //   Credit SellerPending (liability increase) = sellerAmountCents
        //   Credit PlatformRevenue (revenue increase) = commissionCents
        // Invariant: Total Debits == Total Credits (amountCents == sellerAmountCents + commissionCents)
        platformHoldingAccount.UpdateBalance(amountCents, EntryType.Credit);
        sellerAccount.UpdateBalance(sellerAmountCents, EntryType.Credit);
        platformRevenueAccount.UpdateBalance(commissionCents, EntryType.Credit);

        var refId = referenceId.ToString();
        _context.LedgerEntries.AddRange(
            LedgerEntry.Create(platformHoldingAccount.Id, transactionId, amountCents, EntryType.Debit, description, refId, commissionRateBps),
            LedgerEntry.Create(sellerAccount.Id, transactionId, sellerAmountCents, EntryType.Credit, description, refId, commissionRateBps),
            LedgerEntry.Create(platformRevenueAccount.Id, transactionId, commissionCents, EntryType.Credit, $"Commission: {description}", refId, commissionRateBps));

        // DO NOT call SaveChangesAsync here when running inside a MassTransit consumer.
        // The EF Outbox commits all changes atomically.
        // When called from non-consumer code (e.g. MatureFundsCommand), the caller manages the transaction.

        _logger.LogInformation("Ledger credit for seller {SellerId}: amountCents={AmountCents}, commissionCents={CommissionCents}, sellerCents={SellerCents}, ref={ReferenceId}",
            sellerId, amountCents, commissionCents, sellerAmountCents, referenceId);
    }

    /// <summary>
    /// Reverses a seller credit (e.g., on refund). Idempotent by referenceId.
    /// Same transaction rules as CreditSellerAsync — no nested transactions.
    /// </summary>
    public async Task DebitSellerAsync(Guid sellerId, long amountCents, string currency, Guid referenceId, string description, CancellationToken ct = default)
    {
        if (amountCents <= 0) throw new ArgumentException("Amount must be positive", nameof(amountCents));

        var refId = $"REFUND:{referenceId}";

        var alreadyProcessed = await _context.LedgerEntries.AnyAsync(e => e.ReferenceId == refId, ct);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Ledger debit for reference {ReferenceId} already processed — skipping", referenceId);
            return;
        }

        // Look up the original commission rate from the credit entry to ensure
        // refund debits use the same rate. This prevents double-entry drift when
        // the seller's commission rate has changed between the original payment and refund.
        var originalEntry = await _context.LedgerEntries
            .Where(e => e.ReferenceId == referenceId.ToString() && e.CommissionRateBps != null)
            .Select(e => new { e.CommissionRateBps })
            .FirstOrDefaultAsync(ct);

        decimal commissionRate;
        if (originalEntry?.CommissionRateBps != null)
        {
            commissionRate = originalEntry.CommissionRateBps.Value / 100m;
        }
        else
        {
            // Fallback for entries created before CommissionRateBps was introduced
            var profile = await _context.SellerProfiles.Where(p => p.SellerId == sellerId).FirstOrDefaultAsync(ct);
            commissionRate = profile?.CommissionPercentage ?? 10.00m;
            _logger.LogWarning("No CommissionRateBps snapshot found for reference {ReferenceId}, using current rate {Rate}%", referenceId, commissionRate);
        }
        var commissionRateBps = (int)(commissionRate * 100m);
        var commissionCents = (long)(amountCents * commissionRate / 100m);
        var sellerAmountCents = amountCents - commissionCents;

        var transactionId = Guid.CreateVersion7();

        // Deterministic: find the account that was originally credited for this reference
        var creditedAccountId = await _context.LedgerEntries
            .Where(e => e.ReferenceId == referenceId.ToString())
            .Join(_context.LedgerAccounts, e => e.AccountId, a => a.Id, (e, a) => new { Entry = e, Account = a })
            .Where(x => x.Account.OwnerId == sellerId &&
                         (x.Account.Type == AccountType.SellerPending || x.Account.Type == AccountType.SellerPayable))
            .Select(x => x.Account.Id)
            .FirstOrDefaultAsync(ct);

        var sellerAccount = creditedAccountId != Guid.Empty
            ? await LockAccountById(creditedAccountId, ct)
            : null;

        var platformHoldingAccount = await LockAccount(SystemPlatformId, AccountType.PlatformHolding, currency, ct);
        var platformRevenueAccount = await LockAccount(SystemPlatformId, AccountType.PlatformRevenue, currency, ct);

        if (sellerAccount == null || platformHoldingAccount == null || platformRevenueAccount == null)
        {
            _logger.LogWarning("Cannot debit seller {SellerId} — accounts not found", sellerId);
            return;
        }

        // Reverse the credit (balanced double-entry):
        sellerAccount.UpdateBalance(sellerAmountCents, EntryType.Debit);
        platformRevenueAccount.UpdateBalance(commissionCents, EntryType.Debit);
        platformHoldingAccount.UpdateBalance(amountCents, EntryType.Debit);

        _context.LedgerEntries.AddRange(
            LedgerEntry.Create(sellerAccount.Id, transactionId, sellerAmountCents, EntryType.Debit, description, refId, commissionRateBps),
            LedgerEntry.Create(platformRevenueAccount.Id, transactionId, commissionCents, EntryType.Debit, $"Commission reversal: {description}", refId, commissionRateBps),
            LedgerEntry.Create(platformHoldingAccount.Id, transactionId, amountCents, EntryType.Credit, description, refId, commissionRateBps));

        _logger.LogInformation("Ledger debit for seller {SellerId}: amountCents={AmountCents}, ref={ReferenceId}", sellerId, amountCents, referenceId);
    }

    public Task<bool> HasCreditForReferenceAsync(Guid referenceId, CancellationToken ct = default)
    {
        return _context.LedgerEntries.AnyAsync(e => e.ReferenceId == referenceId.ToString(), ct);
    }

    public async Task<long> GetBalanceAsync(Guid sellerId, AccountType type, string currency, CancellationToken ct = default)
    {
        var account = await _context.LedgerAccounts
            .AsNoTracking()
            .Where(a => a.OwnerId == sellerId && a.Type == type && a.Currency == currency)
            .FirstOrDefaultAsync(ct);
        return account?.BalanceCents ?? 0;
    }

    /// <summary>
    /// Loads account with FOR UPDATE lock to prevent concurrent balance corruption.
    /// Creates the account if it doesn't exist (first credit for this owner/type/currency).
    /// </summary>
    private async Task<LedgerAccount> GetOrCreateAccountWithLock(Guid ownerId, AccountType type, string currency, CancellationToken ct)
    {
        var typeInt = (int)type;

        LedgerAccount? account;
        if (_useLinqFallback)
        {
            account = await _context.LedgerAccounts
                .Where(a => a.OwnerId == ownerId && a.Type == type && a.Currency == currency)
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            account = await _context.LedgerAccounts
                .FromSqlRaw(
                    """
                    SELECT *, xmin FROM payouts."LedgerAccounts"
                    WHERE "OwnerId" = {0} AND "Type" = {1} AND "Currency" = {2}
                    FOR UPDATE
                    """,
                    ownerId, typeInt, currency)
                .Where(x => true)
                .FirstOrDefaultAsync(ct);
        }

        if (account == null)
        {
            account = LedgerAccount.Create(ownerId, type, currency);
            _context.LedgerAccounts.Add(account);

            // OUTBOX-SAFE: Flush to DB so the row exists for the FOR UPDATE re-lock.
            // This is safe inside the ambient outbox transaction — it does not commit.
            await _context.SaveChangesAsync(ct); // OUTBOX-SAFE

            if (!_useLinqFallback)
            {
                // Re-lock the newly created row
                account = await _context.LedgerAccounts
                    .FromSqlRaw(
                        """
                        SELECT *, xmin FROM payouts."LedgerAccounts"
                        WHERE "OwnerId" = {0} AND "Type" = {1} AND "Currency" = {2}
                        FOR UPDATE
                        """,
                        ownerId, typeInt, currency)
                    .Where(x => true)
                    .FirstAsync(ct);
            }
        }

        return account;
    }

    private Task<LedgerAccount?> LockAccount(Guid ownerId, AccountType type, string currency, CancellationToken ct)
    {
        var typeInt = (int)type;
        if (_useLinqFallback)
        {
            return _context.LedgerAccounts
                .Where(a => a.OwnerId == ownerId && a.Type == type && a.Currency == currency)
                .FirstOrDefaultAsync(ct);
        }
        return _context.LedgerAccounts
            .FromSqlRaw(
                """
                SELECT *, xmin FROM payouts."LedgerAccounts"
                WHERE "OwnerId" = {0} AND "Type" = {1} AND "Currency" = {2}
                FOR UPDATE
                """,
                ownerId, typeInt, currency)
            .Where(x => true)
            .FirstOrDefaultAsync(ct);
    }

    private Task<LedgerAccount?> LockAccountById(Guid accountId, CancellationToken ct)
    {
        if (_useLinqFallback)
        {
            return _context.LedgerAccounts
                .Where(a => a.Id == accountId)
                .FirstOrDefaultAsync(ct);
        }
        return _context.LedgerAccounts
            .FromSqlRaw(
                """
                SELECT *, xmin FROM payouts."LedgerAccounts"
                WHERE "Id" = {0}
                FOR UPDATE
                """,
                accountId)
            .Where(x => true)
            .FirstOrDefaultAsync(ct);
    }
}
