using Haworks.BuildingBlocks.Persistence;
using Haworks.Payouts.Domain.Enums;

namespace Haworks.Payouts.Domain.Aggregates;

public sealed class LedgerEntry : AuditableEntity
{
    public required Guid AccountId { get; init; }
    public required Guid TransactionId { get; init; }
    public required long AmountCents { get; init; }
    public required EntryType Type { get; init; }
    public required string Description { get; init; }
    public required string ReferenceId { get; init; } // External reference (Order, Payout ID)

    public static LedgerEntry Create(Guid accountId, Guid transactionId, long amountCents, EntryType type, string description, string referenceId)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("AccountId required", nameof(accountId));
        if (transactionId == Guid.Empty) throw new ArgumentException("TransactionId required", nameof(transactionId));
        if (amountCents <= 0) throw new ArgumentException("Amount must be positive", nameof(amountCents));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TransactionId = transactionId,
            AmountCents = amountCents,
            Type = type,
            Description = description,
            ReferenceId = referenceId
        };
    }
}
