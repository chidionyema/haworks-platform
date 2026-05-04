using Haworks.BuildingBlocks.Persistence;

namespace Haworks.Orders.Domain;

public sealed record StockReservation(Guid ProductId, int Quantity);

public sealed class StockReleaseFailure : AuditableEntity
{
    private readonly List<StockReservation> _items = [];

    private StockReleaseFailure() : base() { }

    private StockReleaseFailure(
        Guid id,
        Guid orderId,
        IEnumerable<StockReservation> items,
        string errorMessage)
        : base()
    {
        Id = id;
        OrderId = orderId;
        _items.AddRange(items);
        ErrorMessage = errorMessage;
        FailedAt = DateTimeOffset.UtcNow;
        AttemptCount = 0;
        Status = StockReleaseFailureStatus.Pending;
    }

    public Guid OrderId { get; private set; }
    public IReadOnlyCollection<StockReservation> Items => _items.AsReadOnly();
    public DateTimeOffset FailedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public StockReleaseFailureStatus Status { get; private set; }

    public static StockReleaseFailure Create(
        Guid orderId,
        IEnumerable<StockReservation> items,
        string errorMessage)
    {
        return new StockReleaseFailure(
            id: Guid.NewGuid(),
            orderId: orderId,
            items: items,
            errorMessage: errorMessage);
    }

    public void RecordFailedAttempt(string errorMessage)
    {
        AttemptCount++;
        LastAttemptAt = DateTimeOffset.UtcNow;
        ErrorMessage = errorMessage;
    }

    public void MarkRecovered()
    {
        Status = StockReleaseFailureStatus.Recovered;
        LastAttemptAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = StockReleaseFailureStatus.Failed;
        LastAttemptAt = DateTimeOffset.UtcNow;
    }
}

public enum StockReleaseFailureStatus
{
    Pending = 0,
    Recovered = 1,
    Failed = 2
}
