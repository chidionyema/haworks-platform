using FluentValidation;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Merchant.Application.Common.Interfaces;
using Haworks.Merchant.Application.Merchants.DTOs;
using Haworks.Merchant.Domain.Aggregates;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Haworks.Merchant.Application.Merchants.Commands.SetOperatingHours;

public record SetOperatingHoursCommand(
    Guid MerchantId,
    Guid UserId,
    List<OperatingHourDto> Hours,
    string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result>;

public class SetOperatingHoursCommandValidator : AbstractValidator<SetOperatingHoursCommand>
{
    public SetOperatingHoursCommandValidator()
    {
        RuleFor(v => v.MerchantId).NotEmpty();
        RuleFor(v => v.Hours).NotNull();
        RuleFor(v => v.Hours).Must(h => h.Count <= 7).WithMessage("Maximum 7 entries allowed (one per day).");
        RuleFor(v => v.Hours).Must(h => h.Select(x => x.Day).Distinct().Count() == h.Count)
            .WithMessage("Duplicate DayOfWeek entries are not allowed.");
        RuleForEach(v => v.Hours).ChildRules(hour =>
        {
            hour.RuleFor(h => h.Day).IsInEnum();
            hour.RuleFor(h => h)
                .Must(h => h.Open == TimeSpan.Zero && h.Close == TimeSpan.Zero || // 24h operation
                          h.Open < h.Close || // Normal hours (same day)
                          h.Open > h.Close)   // Wrap-around hours (past midnight)
                .WithMessage("Invalid operating hours. Use 00:00/00:00 for 24h operation, or specify valid open/close times.");
        });
    }
}

public sealed class SetOperatingHoursCommandHandler : IRequestHandler<SetOperatingHoursCommand, Result>
{
    private readonly IMerchantDbContext _context;

    public SetOperatingHoursCommandHandler(IMerchantDbContext context) => _context = context;

    public async Task<Result> Handle(SetOperatingHoursCommand request, CancellationToken cancellationToken)
    {
        var merchantOwner = await _context.Merchants
            .Where(m => m.Id == request.MerchantId)
            .Select(m => m.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (merchantOwner == Guid.Empty)
            return Result.Failure(Error.NotFound("Merchant.NotFound", "Merchant not found."));

        if (merchantOwner != request.UserId)
            return Result.Failure(Error.Forbidden("Merchant.Forbidden", "You are not authorized to update this merchant."));

        var existing = await _context.OperatingHours
            .Where(h => h.MerchantId == request.MerchantId)
            .ToListAsync(cancellationToken);

        _context.OperatingHours.RemoveRange(existing);

        foreach (var dto in request.Hours)
        {
            var hours = OperatingHours.Create(
                request.MerchantId,
                (int)dto.Day,
                dto.Open,
                dto.Close,
                dto.IsOpen);
            _context.OperatingHours.Add(hours);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
