using Siga.Application.Features.Auth.Login;

namespace Siga.Api.Contracts.Auth;

/// <summary>Credenciales de inicio de sesión.</summary>
/// <param name="UserName">Usuario del sistema (máx. 20 caracteres).</param>
/// <param name="Password">Contraseña.</param>
/// <param name="DeviceId">Identificador del dispositivo móvil (opcional, recomendado).</param>
public sealed record LoginRequest(string UserName, string Password, string? DeviceId)
{
    public LoginCommand ToCommand() => new(UserName ?? string.Empty, Password ?? string.Empty, DeviceId);
}
