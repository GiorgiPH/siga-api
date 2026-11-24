namespace Siga.Application.Features.Auth;

public sealed record UserProfileResponse(string UserName, string? Name, string? UserType);

public sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserProfileResponse User)
{
#pragma warning disable CA1822 // AJUSTE: propiedad de instancia para que se serialice en el JSON
    public string TokenType => "Bearer";
#pragma warning restore CA1822
}
