namespace Haworks.Contracts.Rotation;

/// <summary>
/// Published after a PKI certificate is issued/renewed.
/// </summary>
public sealed record CertificateRotatedEvent : DomainEvent
{
    public required string ServiceName { get; init; }
    public required string CommonName { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string SerialNumber { get; init; }
}
