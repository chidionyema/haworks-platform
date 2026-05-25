using System.Text.Json;
using Haworks.Pricing.Application.Interfaces;
using Haworks.Pricing.Application.Services;
using Haworks.Pricing.Domain.Entities;
using Haworks.Pricing.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Haworks.Pricing.Application.Queries;

/// <summary>
/// Handles price calculation following the spec's 10-step pipeline.
/// </summary>
public sealed class CalculateEffectivePriceQueryHandler : IRequestHandler<CalculateEffectivePriceQuery, PriceBreakdownResult>
{
    private readonly ICatalogPricingClient _catalogClient;
    private readonly IPriceRuleRepository _priceRuleRepo;
    private readonly IPromotionCodeRepository _promoCodeRepo;
    private readonly ITaxCalculator _taxCalculator;
    private readonly ICalculationLogRepository _logRepo;
    private readonly PriceCalculationEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CalculateEffectivePriceQueryHandler> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public CalculateEffectivePriceQueryHandler(
        ICatalogPricingClient catalogClient,
        IPriceRuleRepository priceRuleRepo,
        IPromotionCodeRepository promoCodeRepo,
        ITaxCalculator taxCalculator,
        ICalculationLogRepository logRepo,
        PriceCalculationEngine engine,
        IMemoryCache cache,
        ILogger<CalculateEffectivePriceQueryHandler> logger)
    {
        _catalogClient = catalogClient;
        _priceRuleRepo = priceRuleRepo;
        _promoCodeRepo = promoCodeRepo;
        _taxCalculator = taxCalculator;
        _logRepo = logRepo;
        _engine = engine;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PriceBreakdownResult> Handle(CalculateEffectivePriceQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Step 1: Fetch base price from catalog (60s cache)
        var cacheKey = $"catalog_price_{request.ProductId}";
        if (!_cache.TryGetValue(cacheKey, out Models.CatalogProductDto? product))
        {
            var response = await _catalogClient.GetProductAsync(request.ProductId, ct).ConfigureAwait(false);
            if (response is null || !response.IsSuccess || response.Product is null)
            {
                throw new InvalidOperationException($"Product {request.ProductId} not found in catalog.");
            }
            product = response.Product;
            _cache.Set(cacheKey, product, CacheDuration);
        }

        var currency = product!.Currency;
        var baseUnitPriceCents = product.UnitPriceCents;
        var categoryId = product.CategoryId;

        // Step 2: Load active rules
        var rules = await _priceRuleRepo.GetActiveRulesForProductAsync(
            request.ProductId, categoryId, request.Quantity, now, ct).ConfigureAwait(false);

        // Step 3-5: Load promotion code if provided
        PromotionCode? promoCode = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            promoCode = await _promoCodeRepo.GetByCodeAsync(request.PromoCode.ToUpperInvariant(), ct).ConfigureAwait(false);
            if (promoCode is not null && (!promoCode.CanRedeem(now) || !promoCode.IsApplicableTo(request.ProductId, categoryId)))
            {
                promoCode = null; // Invalid, expired, or not applicable — don't apply
            }
        }

        // Step 6: Run calculation engine (C3 Fix: pass product currency, not hardcoded USD)
        var result = _engine.Calculate(
            request.ProductId,
            request.Quantity,
            baseUnitPriceCents,
            currency,
            categoryId,
            rules,
            promoCode,
            now);

        // Step 7: Calculate tax (tax calculator works in major units)
        var subtotalMajor = result.SubtotalCents / 100m;
        var taxResult = await _taxCalculator.CalculateAsync(
            request.CountryCode, request.StateCode, subtotalMajor, result.Currency, ct).ConfigureAwait(false);

        // Step 8: Assemble final result with tax
        // Convert tax result back to cents
        var taxAmountCents = (long)Math.Round(taxResult.TaxAmount * 100m, 0, MidpointRounding.AwayFromZero);
        var totalCents = result.SubtotalCents + taxAmountCents;

        var finalResult = result with
        {
            SubtotalCents = result.SubtotalCents,
            TaxAmountCents = taxAmountCents,
            TaxRate = taxResult.EffectiveRate,
            TotalCents = totalCents,
        };

        // Step 9: Persist audit log
        var appliedRuleIds = JsonSerializer.Serialize(
            result.Discounts.Where(d => !string.Equals(d.Type, "PromotionCode", StringComparison.Ordinal))
                .Select(d => d.Label).ToList());

        var log = PriceCalculationLog.Create(
            productId: request.ProductId,
            quantity: request.Quantity,
            baseUnitPriceCents: baseUnitPriceCents,
            effectiveUnitPriceCents: result.EffectiveUnitPriceCents,
            subtotalCents: result.SubtotalCents,
            taxAmountCents: taxAmountCents,
            taxRateApplied: taxResult.EffectiveRate,
            totalCents: totalCents,
            currency: result.Currency,
            appliedRuleIds: appliedRuleIds,
            promotionCodeApplied: promoCode?.Code,
            userId: request.UserId,
            countryCode: request.CountryCode,
            stateCode: request.StateCode,
            snapshotProductPriceCents: baseUnitPriceCents);

        await _logRepo.AddAsync(log, ct).ConfigureAwait(false);
        // Do NOT call SaveChangesAsync here — this handler is called from both
        // HTTP controllers (where SaveChanges is needed) and MassTransit consumers
        // (where the outbox commits automatically). Callers must flush explicitly.
        // The PricingRequestedConsumer's outbox commits the log atomically.
        // The PricingController calls SaveChangesAsync after mediator.Send returns.

        _logger.LogInformation(
            "Price calculated for product {ProductId}: base={BaseCents}c, effective={EffectiveCents}c, total={TotalCents}c",
            request.ProductId, baseUnitPriceCents, result.EffectiveUnitPriceCents, totalCents);

        return finalResult with { CalculationId = log.Id };
    }
}
