namespace Haworks.Contracts.Checkout;

/// <summary>
/// Published by an operator to resolve a checkout saga stuck in RequiresReview.
/// Resolution must be "completed" or "abandoned".
///   "completed"  — saga moves to Completed (final). No stock release.
///   "abandoned"  — saga publishes StockReleaseRequested then moves to Abandoned.
/// </summary>
public sealed class ManualResolutionEvent
{
    public Guid SagaId { get; init; }
    public required string Resolution { get; init; }  // "completed" | "abandoned"
    public required string OperatorId { get; init; }
}
