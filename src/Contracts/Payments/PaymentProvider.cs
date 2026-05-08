namespace Haworks.Contracts.Payments;

public enum PaymentProvider
{
    None = 0,
    Stripe = 1,
    PayPal = 2,
    Square = 3,
    Braintree = 4,
}
