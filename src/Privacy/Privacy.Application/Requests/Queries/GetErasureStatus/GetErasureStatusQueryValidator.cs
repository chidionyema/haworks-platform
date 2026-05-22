using FluentValidation;

namespace Haworks.Privacy.Application.Requests.Queries.GetErasureStatus;

public class GetErasureStatusQueryValidator : AbstractValidator<GetErasureStatusQuery>
{
    public GetErasureStatusQueryValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
