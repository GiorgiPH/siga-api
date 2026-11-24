using Siga.Application.Features.Auth.Logout;

namespace Siga.Api.Contracts.Auth;

/// <summary>Refresh token de la sesión a cerrar.</summary>
/// <param name="RefreshToken">Refresh token de la sesión del dispositivo.</param>
public sealed record LogoutRequest(string RefreshToken)
{
    public LogoutCommand ToCommand() => new(RefreshToken ?? string.Empty);
}
