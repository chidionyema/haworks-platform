namespace Haworks.Orders.Domain;

public class GuestOrderInfo
{
    protected GuestOrderInfo() { }

    private GuestOrderInfo(
        Guid orderId,
        string? email,
        string? firstName,
        string? lastName,
        string? address,
        string? city,
        string? state,
        string? postalCode,
        string? country,
        string? phone,
        string orderToken,
        bool isComplete)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Address = address;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        Phone = phone;
        OrderToken = orderToken;
        IsComplete = isComplete;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Order? Order { get; private set; }
    public bool IsComplete { get; private set; }
    public string? Email { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Country { get; private set; }
    public string? Phone { get; private set; }
    public string OrderToken { get; private set; } = string.Empty;

    public static GuestOrderInfo Create(
        Guid orderId,
        string email,
        string firstName,
        string lastName,
        string address,
        string city,
        string state,
        string postalCode,
        string country,
        string? phone,
        string orderToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderToken);

        return new GuestOrderInfo(
            orderId, email, firstName, lastName,
            address, city, state, postalCode, country,
            phone, orderToken, isComplete: true);
    }

    public static GuestOrderInfo CreateStub(Guid orderId, string orderToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderToken);
        return new GuestOrderInfo(
            orderId, null, null, null, null, null, null, null, null, null, orderToken, false);
    }

    public void Complete(
        string email,
        string firstName,
        string lastName,
        string address,
        string city,
        string state,
        string postalCode,
        string country,
        string? phone)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Address = address;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        Phone = phone;
        IsComplete = true;
    }
}
