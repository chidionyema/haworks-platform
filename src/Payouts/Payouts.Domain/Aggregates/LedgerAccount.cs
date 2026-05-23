using Haworks.BuildingBlocks.Persistence;
using Haworks.Payouts.Domain.Enums;

namespace Haworks.Payouts.Domain.Aggregates;

public sealed class LedgerAccount : AuditableEntity
{
    public required Guid OwnerId { get; init; } // System Guid or Seller Guid
    public required AccountType Type { get; init; }
    public required string Currency { get; init; }
    public long BalanceCents { get; private set; }

    public void UpdateBalance(long amountCents, EntryType entryType)
    {
        if (amountCents < 0) throw new ArgumentException("Amount must be non-negative", nameof(amountCents));

        if (entryType == EntryType.Credit)
        {
            BalanceCents += amountCents;
        }
        else
        {
            if (BalanceCents < amountCents)
            {
                throw new InvalidOperationException($"Insufficient balance in ledger account {Id}. Available: {BalanceCents}, Required: {amountCents}");
            }
            BalanceCents -= amountCents;
        }
    }

    public static LedgerAccount Create(Guid ownerId, AccountType type, string currency)
    {
        return new LedgerAccount
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Type = type,
            Currency = currency,
            BalanceCents = 0
        };
    }
}
