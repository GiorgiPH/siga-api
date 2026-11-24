using FluentValidation;

namespace Siga.Application.Features.Auth.Login;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("El usuario es obligatorio.")
            .MaximumLength(20).WithErrorCode("MAX_LENGTH").WithMessage("El usuario no puede exceder 20 caracteres.");

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode("REQUIRED").WithMessage("La contraseña es obligatoria.")
            .MaximumLength(128).WithErrorCode("MAX_LENGTH").WithMessage("La contraseña es demasiado larga.");

        RuleFor(x => x.DeviceId)
            .MaximumLength(100).WithErrorCode("MAX_LENGTH").WithMessage("El identificador de dispositivo es demasiado largo.");
    }
}
