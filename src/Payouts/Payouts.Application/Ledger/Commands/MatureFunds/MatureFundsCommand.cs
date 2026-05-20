using Haworks.Payouts.Application.Common.Interfaces;
using Haworks.Payouts.Domain.Aggregates;
using Haworks.Payouts.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.Payouts.Application.Ledger.Commands.MatureFunds;

public record MatureFundsCommand(string IdempotencyKey = "") : IRequest;

public class MatureFundsCommandHandler : IRequestHandler<MatureFundsCommand>
{
    private readonly IPayoutsDbContext _context;
    private readonly ILogger<MatureFundsCommandHandler> _logger;

    public MatureFundsCommandHandler(IPayoutsDbContext context, ILogger<MatureFundsCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Handle(MatureFundsCommand request, CancellationToken cancellationToken)
    {
        // C3 Fix: Wrap in explicit transaction so FOR UPDATE locks are held until commit
        var dbContext = (DbContext)_context;
        await using var tx = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);

        try
        {
            var pendingAccounts = await _context.LedgerAccounts
                .FromSqlRaw(
                    """
                    SELECT *, xmin FROM payouts."LedgerAccounts"
                    WHERE "Type" = {0} AND "Balance" > 0
                    ORDER BY "Id" ASC
                    FOR UPDATE SKIP LOCKED
                    LIMIT 500
                    """,
                    (int)AccountType.SellerPending)
                .ToListAsync(cancellationToken);

            if (pendingAccounts.Count == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return;
            }

            var ownerIds = pendingAccounts.Select(a => a.OwnerId).Distinct().ToList();

            // Lock payable accounts too to prevent concurrent DisbursementService reads
            var payableAccounts = await _context.LedgerAccounts
                .FromSqlRaw(
                    """
                    SELECT *, xmin FROM payouts."LedgerAccounts"
                    WHERE "Type" = {0} AND "OwnerId" = ANY({1})
                    FOR UPDATE
                    """,
                    (int)AccountType.SellerPayable, ownerIds.ToArray())
                .ToDictionaryAsync(a => (a.OwnerId, a.Currency), cancellationToken);

            // M6 Fix: Unique referenceId per maturity batch (prevents constraint violation on 2nd run)
            var batchRef = $"MATURITY:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

            var entriesDbSet = _context.LedgerEntries;
            var maturedCount = 0;
            foreach (var pendingAccount in pendingAccounts)
            {
                var key = (pendingAccount.OwnerId, pendingAccount.Currency);
                if (!payableAccounts.TryGetValue(key, out var payableAccount))
                {
                    payableAccount = LedgerAccount.Create(pendingAccount.OwnerId, AccountType.SellerPayable, pendingAccount.Currency);
                    payableAccounts[key] = payableAccount;
                    _context.LedgerAccounts.Add(payableAccount);
                }

                var amount = pendingAccount.Balance;
                pendingAccount.UpdateBalance(amount, EntryType.Debit);
                payableAccount.UpdateBalance(amount, EntryType.Credit);

                var transactionId = Guid.NewGuid();
                entriesDbSet.AddRange(
                    LedgerEntry.Create(pendingAccount.Id, transactionId, amount, EntryType.Debit, "Funds matured", $"{batchRef}:{pendingAccount.Id}"),
                    LedgerEntry.Create(payableAccount.Id, transactionId, amount, EntryType.Credit, "Funds matured", $"{batchRef}:{payableAccount.Id}"));
                maturedCount++;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Matured funds for {Count} accounts in batch {BatchRef}", maturedCount, batchRef);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogWarning(ex, "Concurrency conflict during fund maturity — another instance handled some accounts");
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
