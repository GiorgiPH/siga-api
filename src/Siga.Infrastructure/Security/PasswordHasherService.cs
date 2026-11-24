using Microsoft.AspNetCore.Identity;
using Siga.Application.Abstractions.Security;
using Siga.Domain.Users;

namespace Siga.Infrastructure.Security;

/// <summary>PBKDF2 (formato V3 de ASP.NET Core Identity) con sal aleatoria por contraseña.</summary>
internal sealed class PasswordHasherService : IPasswordHasher
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    private static readonly Lazy<string> DummyHash = new(() => Hasher.HashPassword(null!, Guid.NewGuid().ToString("N")));

    public string Hash(string password) => Hasher.HashPassword(null!, password);

    public PasswordCheck Verify(string passwordHash, string providedPassword) =>
        Hasher.VerifyHashedPassword(null!, passwordHash, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed
        };

    public void VerifyDummy(string providedPassword) =>
        _ = Hasher.VerifyHashedPassword(null!, DummyHash.Value, providedPassword);
}
