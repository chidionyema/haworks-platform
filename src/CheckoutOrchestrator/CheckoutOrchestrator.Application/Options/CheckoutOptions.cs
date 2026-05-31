using System.ComponentModel.DataAnnotations;

namespace Haworks.CheckoutOrchestrator.Application.Options;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    [Required, Url]
    public string SuccessUrl { get; set; } = string.Empty;

    [Required, Url]
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>
    /// Minutes after stock reservation before the payment session expires.
    /// Defaults to 15. Controls both the MassTransit scheduler delay and
    /// the FailureReason message stored on the saga.
    /// </summary>
    [Range(5, 1440)]
    public int PaymentExpiryMinutes { get; set; } = 15;
}
