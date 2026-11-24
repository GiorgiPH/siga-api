using System.Security.Cryptography;
using System.Text;
using Siga.Application.Abstractions.Security;

namespace Siga.Infrastructure.Security;

/// <summary>
/// Compara contra la contraseña en texto plano de dbo.Usuarios en tiempo constante.
/// Solo se usa en el primer login de cada usuario (migración a hash).
/// VERIFICAR con el sistema de escritorio si su comparación distingue mayúsculas o recorta espacios
/// y replicar exactamente ese comportamiento aquí.
/// </summary>
internal sealed class LegacyPasswordVerifier : ILegacyPasswordVerifier
{
    public bool Verify(string? storedPlainPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(storedPlainPassword)) return false; // usuarios sin contraseña no entran

        var stored = Encoding.UTF8.GetBytes(storedPlainPassword);
        var provided = Encoding.UTF8.GetBytes(providedPassword);
        return CryptographicOperations.FixedTimeEquals(stored, provided);
    }
}
