using Haworks.Analytics.Api.Application.Commands;

namespace Haworks.Analytics.Api.Application.Validators;

public class TrackEventValidator : AbstractValidator<TrackEventCommand>
{
    public TrackEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("EventId must be a valid non-empty GUID.");
        RuleFor(x => x.EventName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId must be a valid non-empty GUID.");
        RuleFor(x => x.OccurredAt).LessThanOrEqualTo(x => DateTime.UtcNow.AddSeconds(60))
            .WithMessage("OccurredAt cannot be more than 60 seconds in the future.");
    }
}
