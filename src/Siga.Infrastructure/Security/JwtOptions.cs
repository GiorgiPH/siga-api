using System.ComponentModel.DataAnnotations;

namespace Siga.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; init; } = default!;
    [Required] public string Audience { get; init; } = default!;
    /// <summary>Mínimo 32 caracteres (256 bits). Viene de user-secrets / variables de entorno / bóveda.</summary>
    [Required, MinLength(32)] public string SigningKey { get; init; } = default!;
    [Range(5, 60)] public int AccessTokenMinutes { get; init; } = 15;
}
