namespace Haworks.Merchant.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to create a merchant for an owner who already has one.
/// </summary>
public sealed class MerchantOwnerAlreadyExistsException : Exception
{
    public Guid OwnerId { get; }

    public MerchantOwnerAlreadyExistsException(Guid ownerId)
        : base($"Owner {ownerId} already has a merchant.")
    {
        OwnerId = ownerId;
    }
}