using System.ComponentModel.DataAnnotations;

namespace Haworks.Notifications.Infrastructure.Channels.Sms.Twilio;

/// <summary>
/// Configuration options for the Twilio SMS provider.
/// Bound from the "Notifications:Providers:Twilio" configuration section.
/// </summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Notifications:Providers:Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;
}
