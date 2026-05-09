namespace Haworks.Catalog.Application.DTOs.Reservations;

/// <summary>
/// Returned by the confirm-reservation endpoint. The OrderId is
/// server-issued in the confirm path (per ADR-004 phase 4) so the caller
/// learns it from the response rather than supplying it.
/// </summary>
public sealed record ConfirmReservationResultDto(
    Guid ReservationId,
    Guid OrderId,
    Guid SagaId);
