using Siga.Application.Features.Auth.RefreshToken;

namespace Siga.Api.Contracts.Auth;

/// <summary>Refresh token emitido en el login o en el último refresh.</summary>
/// <param name="RefreshToken">Refresh token vigente.</param>
public sealed record RefreshTokenRequest(string RefreshToken)
{
    public RefreshTokenCommand ToCommand() => new(RefreshToken ?? string.Empty);
}
