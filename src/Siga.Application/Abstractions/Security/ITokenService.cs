namespace Siga.Application.Abstractions.Security;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(string userName, string? name, string? userType, Guid sessionId);
}
