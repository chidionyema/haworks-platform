using FluentValidation;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Catalog.Domain;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Haworks.Catalog.Application.Commands;

public sealed record CreateProductCommand(
    string Name,
    string Description,
    long UnitPriceCents,
    string Currency,
    Guid CategoryId,
    int InitialStock,
    string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result<Guid>>;


internal sealed class CreateProductCommandHandler(
    IProductRepository products,
    ICategoryRepository categories,
    ILogger<CreateProductCommandHandler> logger
) : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(request.CategoryId, ct);
        if (category is null)
        {
            return Result.Failure<Guid>(Error.Categories.NotFoundWithId(request.CategoryId));
        }

        var product = Product.Create(request.Name, request.Description, request.UnitPriceCents, request.Currency, request.CategoryId);
        if (request.InitialStock > 0)
        {
            product.RestockTo(request.InitialStock);
        }
        product.List();

        await products.AddAsync(product, ct);
        await products.SaveChangesAsync(ct);

        logger.LogInformation("Product {ProductId} created in category {CategoryId}", product.Id, request.CategoryId);
        return Result.Success(product.Id);
    }
}
