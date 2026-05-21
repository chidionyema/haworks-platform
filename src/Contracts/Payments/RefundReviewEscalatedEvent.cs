namespace Haworks.Contracts.Payments;

public sealed record RefundReviewEscalatedEvent : DomainEvent
{
    public required Guid RefundId { get; init; }
    public required Guid OrderId { get; init; }
    public required int HoursInReview { get; init; }
}
