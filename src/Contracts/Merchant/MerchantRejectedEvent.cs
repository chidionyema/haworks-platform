namespace Haworks.Contracts.Merchant;

public sealed record MerchantRejectedEvent : DomainEvent
{
    public required Guid MerchantId { get; init; }
}