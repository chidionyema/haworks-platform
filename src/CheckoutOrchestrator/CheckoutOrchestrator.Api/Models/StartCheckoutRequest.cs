using System.ComponentModel.DataAnnotations;
using Haworks.Contracts.Checkout;

namespace Haworks.CheckoutOrchestrator.Api.Models;

public sealed record StartCheckoutRequest
{
    [Required]
    public required Guid SagaId { get; init; }

    [Required]
    public required Guid OrderId { get; init; }

    public required string UserId { get; init; }

    public required string CustomerEmail { get; init; }

    [Required]
    public required decimal TotalAmount { get; init; }

    public required string IdempotencyKey { get; init; }

    [Required]
    [RegularExpression("^(USD|EUR|GBP|CAD)$", ErrorMessage = "Invalid currency")]
    public required string Currency { get; init; }

    public required IReadOnlyList<CheckoutItemData> Items { get; init; }
}
