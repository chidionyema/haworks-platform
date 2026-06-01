using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Identity.Domain;

public class UserProfile : AuditableEntity
{
    /// <summary>
    /// Protected parameterless constructor for EF Core materialization.
    /// </summary>
    protected UserProfile() : base() { }

    private UserProfile(string userId) : base()
    {
        UserId = userId;
        Country = "US";
    }

    public string UserId { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = "US";
    public string Bio { get; private set; } = string.Empty;
    public string Website { get; private set; } = string.Empty;
    public string AvatarUrl { get; private set; } = string.Empty;
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? LastLogin { get; private set; }
    public User? User { get; private set; }

    /// <summary>
    /// Creates a new user profile.
    /// </summary>
    public static UserProfile Create(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new UserProfile(userId);
    }

    /// <summary>
    /// Updates personal information.
    /// </summary>
    public void UpdatePersonalInfo(string firstName, string lastName, string? phone = null)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name is required", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name is required", nameof(lastName));

        FirstName = firstName;
        LastName = lastName;
        if (phone != null)
        {
            Phone = phone;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates address information.
    /// </summary>
    public void UpdateAddress(string address, string city, string state, string postalCode, string country)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address is required", nameof(address));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required", nameof(city));
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State is required", nameof(state));
        if (string.IsNullOrWhiteSpace(postalCode))
            throw new ArgumentException("Postal code is required", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country))
            throw new ArgumentException("Country is required", nameof(country));

        Address = address;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates profile information (bio, website).
    /// </summary>
    public void UpdateProfileInfo(string? bio = null, string? website = null)
    {
        if (bio != null)
        {
            Bio = bio;
        }
        if (website != null)
        {
            Website = website;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the avatar URL.
    /// </summary>
    public void SetAvatarUrl(string avatarUrl)
    {
        AvatarUrl = avatarUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a login event.
    /// </summary>
    public void RecordLogin()
    {
        LastLogin = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the user reference.
    /// </summary>
    public void SetUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        User = user;
    }
}
