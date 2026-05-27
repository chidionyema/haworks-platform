namespace Haworks.Contracts.Merchant;

public sealed record MerchantSetToPendingEvent : DomainEvent
{
    public required Guid MerchantId { get; init; }
}