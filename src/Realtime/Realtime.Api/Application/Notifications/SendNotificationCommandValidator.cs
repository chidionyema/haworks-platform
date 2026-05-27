using FluentValidation;
using System.Text.Json;

namespace Haworks.Realtime.Api.Application.Notifications;

public class SendNotificationCommandValidator : AbstractValidator<SendNotificationCommand>
{
    public SendNotificationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.MessageType)
            .NotEmpty()
            .Matches("^[A-Za-z][A-Za-z0-9]*$")
            .WithMessage("MessageType must be alphanumeric starting with letter");
        RuleFor(x => x.Data)
            .NotNull()
            .Must(BeSerializableToJson)
            .WithMessage("Data must be JSON serializable");
    }

    private static bool BeSerializableToJson(object data)
    {
        try
        {
            JsonSerializer.Serialize(data);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
