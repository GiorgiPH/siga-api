using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Siga.Application.Abstractions.Security;

namespace Siga.Infrastructure.Security;

internal sealed class RefreshTokenFactory : IRefreshTokenFactory
{
    public GeneratedRefreshToken Create()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64)); // 86 caracteres
        return new GeneratedRefreshToken(token, ComputeHash(token));
    }

    public string ComputeHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))); // 64 caracteres
}
