using FluentValidation;

namespace Siga.Application.Features.Auth.Logout;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() =>
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("El refresh token es obligatorio.")
            .MaximumLength(200).WithErrorCode("MAX_LENGTH").WithMessage("El refresh token no es válido.");
}
