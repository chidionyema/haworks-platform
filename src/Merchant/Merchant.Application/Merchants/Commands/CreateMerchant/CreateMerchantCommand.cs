using FluentValidation;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Contracts.Merchant;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Domain.Aggregates;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Haworks.Merchant.Application.Merchants.Commands.CreateMerchant;

public record CreateMerchantCommand(Guid OwnerId, string Name, string Slug, string IdempotencyKey = "") : IIdempotentCommand, IRequest<Guid>;

public class CreateMerchantCommandValidator : AbstractValidator<CreateMerchantCommand>
{
    public CreateMerchantCommandValidator()
    {
        RuleFor(v => v.OwnerId).NotEmpty();
        RuleFor(v => v.Name).NotEmpty().MaximumLength(200);
        RuleFor(v => v.Slug).NotEmpty().MaximumLength(100).Matches(@"^[a-z0-9-]+$");
    }
}

public class CreateMerchantCommandHandler : IRequestHandler<CreateMerchantCommand, Guid>
{
    private readonly IMerchantDbContext _context;
    private readonly IPublishEndpoint _publishEndpoint;

    public CreateMerchantCommandHandler(IMerchantDbContext context, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Guid> Handle(CreateMerchantCommand request, CancellationToken cancellationToken)
    {
        var merchant = MerchantProfile.Create(request.OwnerId, request.Name, request.Slug);

        _context.Merchants.Add(merchant);

        // Publish BEFORE save — outbox-friendly. The OutboxMessage row commits
        // in the same EF transaction as the merchant insert; on rollback the
        // publish is rolled back too.
        await _publishEndpoint.Publish(new MerchantCreatedEvent
        {
            MerchantId = merchant.Id,
            OwnerId = merchant.OwnerId,
            Name = merchant.Name,
            Slug = merchant.Slug
        }, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            var detail = pgEx.Detail ?? "";
            if (detail.Contains("OwnerId"))
                throw new InvalidOperationException("Owner already has a merchant.", ex);
            if (detail.Contains("Slug"))
                throw new InvalidOperationException("Slug is already in use.", ex);
            throw new InvalidOperationException("Unique constraint violation detected.", ex);
        }

        return merchant.Id;
    }
}
