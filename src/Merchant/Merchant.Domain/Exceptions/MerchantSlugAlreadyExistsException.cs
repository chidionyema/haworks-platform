namespace Haworks.Merchant.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to create a merchant with a slug that already exists.
/// </summary>
public sealed class MerchantSlugAlreadyExistsException : Exception
{
    public string Slug { get; }

    public MerchantSlugAlreadyExistsException(string slug)
        : base($"Slug '{slug}' is already in use.")
    {
        Slug = slug;
    }
}