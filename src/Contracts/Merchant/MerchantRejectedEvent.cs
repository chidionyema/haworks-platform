namespace Haworks.Contracts.Merchant;

public sealed record MerchantRejectedEvent
{
    public required Guid MerchantId { get; init; }
    public required string RejectedBy { get; init; }
    public required string Reason { get; init; }
}