using Haworks.BuildingBlocks.Persistence;
using Haworks.Pricing.Domain.Enums;

namespace Haworks.Pricing.Domain.Entities;

/// <summary>
/// Pricing rule aggregate root. Immutable once applied — rules are soft-deleted (Archived), never mutated after use.
/// </summary>
public sealed class PriceRule : AuditableEntity
{
    private readonly List<TieredPrice> _tieredPrices = new();

    private PriceRule() { }

    public Guid? ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public int Priority { get; private set; }
    public DiscountType DiscountType { get; private set; }
    public decimal DiscountPercentage { get; private set; }
    public long DiscountAmountCents { get; private set; }
    public int MinimumQuantity { get; private set; }
    public int? MaximumQuantity { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string SellerTimezone { get; private set; } = "America/New_York";
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public PriceRuleStatus Status { get; private set; }

    public IReadOnlyList<TieredPrice> TieredPrices => _tieredPrices.AsReadOnly();

    /// <summary>
    /// Factory method enforcing domain invariants.
    /// </summary>
    public static PriceRule Create(
        Guid? productId,
        Guid? categoryId,
        int priority,
        DiscountType discountType,
        decimal discountPercentage = 0,
        long discountAmountCents = 0,
        int minimumQuantity = 0,
        int? maximumQuantity = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? expiresAt = null,
        string sellerTimezone = "America/New_York")
    {
        if (productId is null && categoryId is null)
            throw new ArgumentException("ProductId and CategoryId cannot both be null.");

        if (discountType == DiscountType.Percentage)
        {
            if (discountPercentage <= 0 || discountPercentage > 100)
                throw new ArgumentException("DiscountPercentage must be > 0 and <= 100 for Percentage type.");
        }
        else if (discountType == DiscountType.FixedAmount)
        {
            if (discountAmountCents <= 0)
                throw new ArgumentException("DiscountAmountCents must be > 0 for FixedAmount type.");
        }

        if (minimumQuantity < 0)
            throw new ArgumentException("MinimumQuantity must be >= 0.");

        if (maximumQuantity.HasValue && maximumQuantity.Value <= minimumQuantity)
            throw new ArgumentException("MaximumQuantity must be greater than MinimumQuantity.");

        if (startsAt.HasValue && expiresAt.HasValue && expiresAt.Value <= startsAt.Value)
            throw new ArgumentException("ExpiresAt must be after StartsAt.");

        var now = DateTimeOffset.UtcNow;
        var status = PriceRuleStatus.Active;
        if (startsAt.HasValue && startsAt.Value > now)
            status = PriceRuleStatus.Scheduled;

        return new PriceRule
        {
            ProductId = productId,
            CategoryId = categoryId,
            Priority = priority,
            DiscountType = discountType,
            DiscountPercentage = discountPercentage,
            DiscountAmountCents = discountAmountCents,
            MinimumQuantity = minimumQuantity,
            MaximumQuantity = maximumQuantity,
            StartsAt = startsAt,
            ExpiresAt = expiresAt,
            SellerTimezone = sellerTimezone,
            IsActive = status == PriceRuleStatus.Active,
            IsDeleted = false,
            Status = status,
        };
    }

    /// <summary>
    /// Add a tiered price. Tiers must not overlap.
    /// </summary>
    public void AddTier(int fromQuantity, int? toQuantity, long unitPriceCents)
    {
        if (fromQuantity < 0)
            throw new ArgumentException("FromQuantity must be >= 0.");

        if (toQuantity.HasValue && toQuantity.Value < fromQuantity)
            throw new ArgumentException("ToQuantity must be >= FromQuantity.");

        if (unitPriceCents < 0)
            throw new ArgumentException("UnitPriceCents must be >= 0.");

        // Check overlap
        foreach (var existing in _tieredPrices)
        {
            var existingTo = existing.ToQuantity ?? int.MaxValue;
            var newTo = toQuantity ?? int.MaxValue;

            if (fromQuantity <= existingTo && newTo >= existing.FromQuantity)
                throw new InvalidOperationException(
                    $"Tier [{fromQuantity}-{toQuantity}] overlaps with existing tier [{existing.FromQuantity}-{existing.ToQuantity}].");
        }

        _tieredPrices.Add(TieredPrice.Create(Id, fromQuantity, toQuantity, unitPriceCents));
    }

    /// <summary>
    /// Checks if this rule is applicable for the given context at the specified time.
    /// </summary>
    public bool IsApplicableTo(Guid productId, Guid? categoryId, int quantity, DateTimeOffset now)
    {
        if (!IsActive || IsDeleted || Status == PriceRuleStatus.Archived)
            return false;

        if (StartsAt.HasValue && now < StartsAt.Value)
            return false;

        if (ExpiresAt.HasValue && now >= ExpiresAt.Value)
            return false;

        if (quantity < MinimumQuantity)
            return false;

        if (MaximumQuantity.HasValue && quantity > MaximumQuantity.Value)
            return false;

        // Scope matching: ProductId match > CategoryId match > (should not be null/null per invariant)
        if (ProductId.HasValue && ProductId.Value != productId)
            return false;

        if (!ProductId.HasValue && CategoryId.HasValue && categoryId.HasValue && CategoryId.Value != categoryId.Value)
            return false;

        return true;
    }

    /// <summary>
    /// Soft-delete. Applied rules can never be modified, only superseded.
    /// </summary>
    public void Archive()
    {
        IsActive = false;
        IsDeleted = true;
        Status = PriceRuleStatus.Archived;
        LastModifiedDate = DateTime.UtcNow;
    }

    /// <summary>
    /// Activate a scheduled rule.
    /// </summary>
    public void Activate()
    {
        if (Status == PriceRuleStatus.Archived)
            throw new InvalidOperationException("Cannot activate an archived rule.");

        IsActive = true;
        Status = PriceRuleStatus.Active;
        LastModifiedDate = DateTime.UtcNow;
    }
}
