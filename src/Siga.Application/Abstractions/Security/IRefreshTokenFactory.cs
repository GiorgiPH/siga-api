namespace Siga.Application.Abstractions.Security;

public sealed record GeneratedRefreshToken(string Token, string Hash);

public interface IRefreshTokenFactory
{
    GeneratedRefreshToken Create();
    string ComputeHash(string token);
}
