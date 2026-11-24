namespace Siga.Domain.Users;

/// <summary>Usuario del sistema de escritorio (dbo.Usuarios). Solo lectura para la API.</summary>
public sealed class SystemUser
{
    private SystemUser() { }

    public string UserName { get; private set; } = default!;
    /// <summary>Contraseña en texto plano del sistema legado. Solo se usa en el primer login.</summary>
    public string? LegacyPassword { get; private set; }
    public string? Name { get; private set; }
    public string? UserType { get; private set; }
    public string? Status { get; private set; }
}
