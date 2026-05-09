namespace Haworks.Catalog.Application.DTOs.Reservations;

/// <summary>
/// Payload for a single line on a reservation request/response. Mirrors
/// <see cref="Haworks.Contracts.Catalog.StockReservationItem"/> but is the
/// API-layer DTO rather than the cross-context contract — keeps the HTTP
/// surface stable when the contract evolves.
/// </summary>
public sealed record ReservationItemDto(
    Guid ProductId,
    string ProductName,
    int Quantity);

/// <summary>
/// Returned by the create-reservation endpoint. <see cref="IsExisting"/>
/// is reserved for future client-side idempotency hooks; today the
/// platform's <c>IdempotencyMiddleware</c> short-circuits replays to 409
/// before the handler runs, so this field is always <c>false</c> on the
/// success path.
/// </summary>
public sealed record ReservationDto(
    Guid ReservationId,
    IReadOnlyList<ReservationItemDto> Items,
    DateTimeOffset ExpiresAt,
    bool IsExisting);
