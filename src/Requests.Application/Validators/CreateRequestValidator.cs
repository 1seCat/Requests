using FluentValidation;
using Requests.Application.DTOs;
namespace Requests.Application.Validators;
public sealed class CreateRequestValidator : AbstractValidator<CreateRequestRequest>
{
    public CreateRequestValidator()
    {
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
public sealed class UpdateStatusValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<Requests.Domain.Enums.RequestStatus>(s, true, out _))
            .WithMessage("Unknown status.");
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}